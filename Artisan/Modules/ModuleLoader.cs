using System.Reflection;
using Microsoft.Extensions.Configuration;
using Artisan.Attributes;
using Artisan.Modules;

namespace Artisan.Core.Modules;

public class ModuleLoader
{
    private readonly IReadOnlyCollection<Type> _disabledModules;

    public ModuleLoader(IReadOnlyCollection<Type> disabledModules)
    {
        _disabledModules = disabledModules ?? new HashSet<Type>();
    }

    /// <summary>
    /// 加载并排序模块
    /// </summary>
    /// <param name="moduleTypes">扫描到的所有模块类型</param>
    /// <param name="config">配置（用于 ShouldLoad 判断）</param>
    /// <returns>排序好的模块实例列表</returns>
    public List<ArtisanModule> LoadModulesFromTypes(IEnumerable<Type> moduleTypes, IConfiguration config)
    {
        // 1. 实例化所有模块并初步过滤
        var nodes = new List<ModuleNode>();
        
        foreach (var type in moduleTypes)
        {
            // A. 检查黑名单
            if (_disabledModules.Contains(type)) continue;

            // B. 实例化
            // 这里支持模块通过构造函数注入 IConfiguration
            var instance = CreateModuleInstance(type, config);

            // C. 检查条件加载 (ShouldLoad)
            if (instance != null && instance.ShouldLoad(config))
            {
                var attr = type.GetCustomAttribute<ModuleAttribute>() ?? new ModuleAttribute();
                nodes.Add(new ModuleNode(type, instance, attr));
            }
        }

        // 2. 构建依赖图 (显式 + 隐式)
        BuildDependencyGraph(nodes);

        // 3. 拓扑排序
        try
        {
            return SortByDependency(nodes);
        }
        catch (CircularDependencyException ex)
        {
            // 这里抛出更友好的错误信息，帮助用户排查
            throw new InvalidOperationException($"Module loading failed due to circular dependency: {ex.Message}", ex);
        }
    }

    private ArtisanModule? CreateModuleInstance(Type type, IConfiguration config)
    {
        try
        {
            // 简单支持构造函数注入 IConfiguration
            // 如果构造函数需要 IConfiguration，则注入；否则调用无参构造
            var ctorWithConfig = type.GetConstructor(new[] { typeof(IConfiguration) });
            if (ctorWithConfig != null)
            {
                return (ArtisanModule)ctorWithConfig.Invoke(new object[] { config });
            }

            return (ArtisanModule?)Activator.CreateInstance(type);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to instantiate module '{type.FullName}'. Ensure it has a public parameterless constructor or one accepting IConfiguration.", ex);
        }
    }

    private void BuildDependencyGraph(List<ModuleNode> nodes)
    {
        // 创建快速查找字典
        var typeToNodeMap = nodes.ToDictionary(n => n.Type, n => n);

        foreach (var dependentNode in nodes)
        {
            // A. 处理显式依赖 [DependsOn]
            foreach (var dependencyType in dependentNode.Attribute.DependsOn)
            {
                if (typeToNodeMap.TryGetValue(dependencyType, out var dependencyNode))
                {
                    dependentNode.Dependencies.Add(dependencyNode);
                }
            }

            // B. 处理隐式依赖 (程序集引用推导)
            // 逻辑：如果 ModuleA 所在的程序集引用了 ModuleB 所在的程序集，则 A 依赖 B
            var referencedAssemblyNames = dependentNode.Type.Assembly.GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToHashSet();

            foreach (var potentialDependency in nodes)
            {
                // 跳过自己
                if (potentialDependency == dependentNode) continue;

                // 如果 potentialDependency 已经在显式依赖里了，跳过
                if (dependentNode.Dependencies.Contains(potentialDependency)) continue;

                var depAssemblyName = potentialDependency.Type.Assembly.GetName().Name;
                
                // 如果存在物理引用关系，建立逻辑依赖
                if (depAssemblyName != null && referencedAssemblyNames.Contains(depAssemblyName))
                {
                    dependentNode.Dependencies.Add(potentialDependency);
                }
            }
        }
    }

    private List<ArtisanModule> SortByDependency(List<ModuleNode> nodes)
    {
        var sorted = new List<ArtisanModule>();
        var visited = new HashSet<ModuleNode>();
        var visiting = new HashSet<ModuleNode>();

        // 技巧：在进行拓扑排序前，先按 Level 和 Order 对入口节点进行预排序。
        // 这样在没有依赖关系的情况下，DFS 会按照我们期望的优先级顺序访问节点。
        // OrderBy 是升序：Kernel(0) -> Application(20)
        var preSortedNodes = nodes
            .OrderBy(n => n.Attribute.Level)
            .ThenBy(n => n.Attribute.Order)
            .ToList();

        foreach (var node in preSortedNodes)
        {
            Visit(node, sorted, visited, visiting);
        }

        return sorted;
    }

    private void Visit(ModuleNode node, List<ArtisanModule> sorted, HashSet<ModuleNode> visited, HashSet<ModuleNode> visiting)
    {
        if (visited.Contains(node)) return;

        if (visiting.Contains(node))
        {
            // 发现循环依赖
            // 为了展示闭环，我们简单抛出异常
            throw new CircularDependencyException($"Circular dependency detected involving {node.Type.Name}");
        }

        visiting.Add(node);

        // 递归访问所有依赖项 (先加载依赖)
        // 这里也对依赖项进行预排序，保证同级依赖按 Order 加载
        var sortedDependencies = node.Dependencies
            .OrderBy(n => n.Attribute.Level)
            .ThenBy(n => n.Attribute.Order);

        foreach (var dep in sortedDependencies)
        {
            Visit(dep, sorted, visited, visiting);
        }

        visiting.Remove(node);
        visited.Add(node);

        // 只有当依赖都处理完了，才把自己加入列表
        sorted.Add(node.Instance);
    }

    /// <summary>
    /// 内部类：用于封装排序过程中的元数据
    /// </summary>
    private class ModuleNode
    {
        public Type Type { get; }
        public ArtisanModule Instance { get; }
        public ModuleAttribute Attribute { get; }
        public List<ModuleNode> Dependencies { get; } = new();

        public ModuleNode(Type type, ArtisanModule instance, ModuleAttribute attribute)
        {
            Type = type;
            Instance = instance;
            Attribute = attribute;
        }
    }
}

public class CircularDependencyException : Exception
{
    public CircularDependencyException(string message) : base(message) { }
}