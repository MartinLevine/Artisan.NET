using System.Reflection;
using Artisan.Application;
using Artisan.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.Modules;

/// <summary>
/// 模块加载器 - 自动发现模块并基于程序集引用推导依赖关系
/// </summary>
public class ModuleLoader
{
    // 缓存：程序集 -> 该程序集中的模块类型
    private readonly Dictionary<Assembly, Type?> _assemblyModuleCache = new();

    // 依赖图：模块类型 -> 依赖的模块类型列表
    private readonly Dictionary<Type, List<Type>> _dependencyGraph = new();

    // 框架配置选项
    private ArtisanOptions _options = new();

    /// <summary>
    /// 加载所有模块（自动发现 + 程序集引用推导）
    /// </summary>
    public List<ArtisanModule> LoadModules(
        Assembly entryAssembly,
        IServiceCollection services,
        IConfiguration configuration,
        ArtisanOptions? options = null)
    {
        _options = options ?? new ArtisanOptions();
        _assemblyModuleCache.Clear();
        _dependencyGraph.Clear();

        // 1. 递归扫描所有引用的程序集，发现所有模块
        var allModuleTypes = FindAllModulesInReferencedAssemblies(entryAssembly);

        // 2. 过滤被禁用的模块
        allModuleTypes = FilterDisabledModules(allModuleTypes);

        // 3. 应用模块替换
        allModuleTypes = ApplyModuleReplacements(allModuleTypes);

        // 4. 构建隐式依赖图（基于程序集引用关系）
        BuildImplicitDependencyGraph(allModuleTypes);

        // 5. 添加显式依赖（DependsOn）
        AddExplicitDependencies(allModuleTypes);

        // 6. 拓扑排序（先按 Level/Order，再按依赖关系）
        var sortedModuleTypes = TopologicalSort(allModuleTypes);

        // 7. 实例化模块（通过 DI 注入配置类）
        return InstantiateModules(sortedModuleTypes, services, configuration);
    }

    /// <summary>
    /// 过滤被禁用的模块
    /// </summary>
    private List<Type> FilterDisabledModules(List<Type> moduleTypes)
    {
        return moduleTypes
            .Where(t => !_options.IsModuleDisabled(t))
            .ToList();
    }

    /// <summary>
    /// 应用模块替换
    /// </summary>
    private List<Type> ApplyModuleReplacements(List<Type> moduleTypes)
    {
        var result = new List<Type>();

        foreach (var moduleType in moduleTypes)
        {
            var replacement = _options.GetModuleReplacement(moduleType);
            if (replacement != null)
            {
                // 使用替换模块，但只添加一次
                if (!result.Contains(replacement))
                {
                    result.Add(replacement);
                }
            }
            else
            {
                result.Add(moduleType);
            }
        }

        // 添加替换模块中那些不在原列表中的模块（新增的替换模块）
        foreach (var replacement in _options.ModuleReplacements.Values)
        {
            if (!result.Contains(replacement))
            {
                result.Add(replacement);
            }
        }

        return result;
    }

    /// <summary>
    /// 递归扫描所有引用的程序集，找到所有包含 ArtisanModule 的模块
    /// </summary>
    private List<Type> FindAllModulesInReferencedAssemblies(Assembly entryAssembly)
    {
        var allModules = new List<Type>();
        var visitedAssemblies = new HashSet<string>();
        var assemblyQueue = new Queue<Assembly>();

        assemblyQueue.Enqueue(entryAssembly);

        while (assemblyQueue.Count > 0)
        {
            var assembly = assemblyQueue.Dequeue();
            var assemblyName = assembly.GetName().Name;

            if (assemblyName == null || visitedAssemblies.Contains(assemblyName))
                continue;

            visitedAssemblies.Add(assemblyName);

            // 跳过系统程序集
            if (IsSystemAssembly(assemblyName))
                continue;

            // 查找该程序集中的模块
            var moduleTypes = FindModulesInAssembly(assembly);
            foreach (var moduleType in moduleTypes)
            {
                allModules.Add(moduleType);
                _assemblyModuleCache[assembly] = moduleType;
            }

            // 递归扫描引用的程序集
            foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
            {
                if (IsSystemAssembly(referencedAssemblyName.Name))
                    continue;

                try
                {
                    var referencedAssembly = Assembly.Load(referencedAssemblyName);
                    assemblyQueue.Enqueue(referencedAssembly);
                }
                catch (Exception)
                {
                    // 忽略无法加载的程序集
                }
            }
        }

        return allModules;
    }

    /// <summary>
    /// 在程序集中查找所有 ArtisanModule 子类
    /// </summary>
    private List<Type> FindModulesInAssembly(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes()
                .Where(t =>
                    t.IsClass &&
                    !t.IsAbstract &&
                    typeof(ArtisanModule).IsAssignableFrom(t))
                .ToList();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // 处理类型加载异常，只返回能加载的类型
            return ex.Types
                .Where(t => t != null &&
                            t.IsClass &&
                            !t.IsAbstract &&
                            typeof(ArtisanModule).IsAssignableFrom(t))
                .Select(t => t!)
                .ToList();
        }
    }

    /// <summary>
    /// 基于程序集引用关系构建隐式依赖图
    /// 如果程序集 A 引用了程序集 B，且两者都有模块，则 ModuleA 依赖 ModuleB
    /// </summary>
    private void BuildImplicitDependencyGraph(List<Type> allModuleTypes)
    {
        // 初始化依赖图
        foreach (var moduleType in allModuleTypes)
        {
            _dependencyGraph[moduleType] = new List<Type>();
        }

        // 构建程序集到模块的映射
        var assemblyToModules = new Dictionary<Assembly, List<Type>>();
        foreach (var moduleType in allModuleTypes)
        {
            if (!assemblyToModules.ContainsKey(moduleType.Assembly))
            {
                assemblyToModules[moduleType.Assembly] = new List<Type>();
            }
            assemblyToModules[moduleType.Assembly].Add(moduleType);
        }

        // 遍历每个模块，检查其程序集引用
        foreach (var moduleType in allModuleTypes)
        {
            var assembly = moduleType.Assembly;

            foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
            {
                try
                {
                    var referencedAssembly = Assembly.Load(referencedAssemblyName);

                    // 如果被引用的程序集也有模块，建立隐式依赖
                    if (assemblyToModules.TryGetValue(referencedAssembly, out var referencedModules))
                    {
                        foreach (var referencedModule in referencedModules)
                        {
                            if (!_dependencyGraph[moduleType].Contains(referencedModule))
                            {
                                _dependencyGraph[moduleType].Add(referencedModule);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // 忽略无法加载的程序集
                }
            }
        }
    }

    /// <summary>
    /// 添加显式依赖（通过 DependsOn 声明）
    /// </summary>
    private void AddExplicitDependencies(List<Type> allModuleTypes)
    {
        foreach (var moduleType in allModuleTypes)
        {
            var moduleAttr = moduleType.GetCustomAttribute<ModuleAttribute>();
            if (moduleAttr?.DependsOn != null)
            {
                foreach (var dependency in moduleAttr.DependsOn)
                {
                    if (_dependencyGraph.ContainsKey(moduleType) &&
                        _dependencyGraph.ContainsKey(dependency) &&
                        !_dependencyGraph[moduleType].Contains(dependency))
                    {
                        _dependencyGraph[moduleType].Add(dependency);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 拓扑排序：先按 Level/Order 分组，再按依赖关系排序
    /// </summary>
    private List<Type> TopologicalSort(List<Type> allModuleTypes)
    {
        // 按 Level 和 Order 排序
        var sortedByLevel = allModuleTypes
            .Select(t => new
            {
                Type = t,
                Attr = t.GetCustomAttribute<ModuleAttribute>() ?? new ModuleAttribute()
            })
            .OrderBy(x => (int)x.Attr.Level)
            .ThenBy(x => x.Attr.Order)
            .Select(x => x.Type)
            .ToList();

        // 在同 Level 内进行拓扑排序
        var result = new List<Type>();
        var visited = new HashSet<Type>();
        var visiting = new HashSet<Type>();

        foreach (var moduleType in sortedByLevel)
        {
            TopologicalSortVisit(moduleType, result, visited, visiting);
        }

        return result;
    }

    private void TopologicalSortVisit(
        Type moduleType,
        List<Type> result,
        HashSet<Type> visited,
        HashSet<Type> visiting)
    {
        if (visited.Contains(moduleType))
            return;

        if (visiting.Contains(moduleType))
        {
            // 检测到循环依赖
            throw new CircularDependencyException(visiting.Append(moduleType).ToArray());
        }

        visiting.Add(moduleType);

        // 先访问依赖
        if (_dependencyGraph.TryGetValue(moduleType, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                TopologicalSortVisit(dependency, result, visited, visiting);
            }
        }

        visiting.Remove(moduleType);
        visited.Add(moduleType);
        result.Add(moduleType);
    }

    /// <summary>
    /// 实例化所有模块
    /// </summary>
    private List<ArtisanModule> InstantiateModules(
        List<Type> sortedModuleTypes,
        IServiceCollection services,
        IConfiguration configuration)
    {
        var modules = new List<ArtisanModule>();
        var tempServiceProvider = BuildTempServiceProvider(services, configuration);

        foreach (var moduleType in sortedModuleTypes)
        {
            var module = (ArtisanModule)ActivatorUtilities.CreateInstance(tempServiceProvider, moduleType);
            modules.Add(module);
        }

        return modules;
    }

    private IServiceProvider BuildTempServiceProvider(IServiceCollection services, IConfiguration configuration)
    {
        var tempServices = new ServiceCollection();
        tempServices.AddSingleton(configuration);
        RegisterAppSettings(tempServices, configuration);
        return tempServices.BuildServiceProvider();
    }

    private void RegisterAppSettings(IServiceCollection services, IConfiguration configuration)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    var appSettingAttr = type.GetCustomAttribute<AppSettingAttribute>();
                    if (appSettingAttr != null)
                    {
                        var section = configuration.GetSection(appSettingAttr.Section);
                        var instance = section.Get(type) ?? Activator.CreateInstance(type);
                        if (instance != null)
                        {
                            services.AddSingleton(type, instance);
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // 忽略无法加载的程序集
            }
        }
    }

    /// <summary>
    /// 判断是否为系统程序集（跳过扫描）
    /// </summary>
    private static bool IsSystemAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return true;

        return assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase);
    }
}
