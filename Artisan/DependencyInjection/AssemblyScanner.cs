using System.Reflection;

namespace Artisan.DependencyInjection;

/// <summary>
/// 程序集扫描器
/// 负责发现和扫描应用程序集及其引用的程序集
/// </summary>
public class AssemblyScanner : IAssemblyScanner
{
    private readonly List<Assembly> _scannedAssemblies = new();
    private readonly List<Type> _scannedTypes = new();
    private readonly HashSet<string> _processedAssemblies = new();

    /// <inheritdoc />
    public IReadOnlyList<Assembly> ScannedAssemblies => _scannedAssemblies;

    /// <inheritdoc />
    public IReadOnlyList<Type> ScannedTypes => _scannedTypes;

    /// <inheritdoc />
    public void Scan(Assembly entryAssembly, IEnumerable<string>? additionalPatterns = null)
    {
        _scannedAssemblies.Clear();
        _scannedTypes.Clear();
        _processedAssemblies.Clear();

        // 获取入口程序集的根命名空间
        var rootNamespace = GetRootNamespace(entryAssembly);
        var patterns = new List<string>();

        if (!string.IsNullOrEmpty(rootNamespace))
        {
            // 添加入口程序集命名空间及其子命名空间
            patterns.Add($"{rootNamespace}.**");
        }

        // 添加 Artisan 框架命名空间
        patterns.Add("Artisan.**");

        // 添加额外的匹配模式
        if (additionalPatterns != null)
        {
            patterns.AddRange(additionalPatterns);
        }

        // 递归扫描入口程序集及其引用
        ScanAssemblyRecursive(entryAssembly, patterns);
    }

    /// <summary>
    /// 递归扫描程序集及其引用
    /// </summary>
    private void ScanAssemblyRecursive(Assembly assembly, List<string> patterns)
    {
        var assemblyName = assembly.GetName().Name;

        if (assemblyName == null || _processedAssemblies.Contains(assemblyName))
            return;

        // 跳过系统程序集
        if (IsSystemAssembly(assemblyName))
            return;

        // 检查程序集是否匹配任一模式
        if (!ShouldScanAssembly(assemblyName, patterns))
            return;

        _processedAssemblies.Add(assemblyName);
        _scannedAssemblies.Add(assembly);

        // 扫描程序集中的类型
        try
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic);

            foreach (var type in types)
            {
                if (ShouldIncludeType(type, patterns))
                {
                    _scannedTypes.Add(type);
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            // 处理类型加载异常，只添加能加载的类型
            var loadedTypes = ex.Types.Where(t => t != null && t.IsClass && !t.IsAbstract && t.IsPublic);
            foreach (var type in loadedTypes)
            {
                if (ShouldIncludeType(type!, patterns))
                {
                    _scannedTypes.Add(type!);
                }
            }
        }

        // 递归扫描引用的程序集
        foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
        {
            if (IsSystemAssembly(referencedAssemblyName.Name))
                continue;

            try
            {
                var referencedAssembly = Assembly.Load(referencedAssemblyName);
                ScanAssemblyRecursive(referencedAssembly, patterns);
            }
            catch (Exception)
            {
                // 忽略无法加载的程序集
            }
        }
    }

    /// <summary>
    /// 检查程序集是否应该被扫描
    /// </summary>
    private bool ShouldScanAssembly(string? assemblyName, List<string> patterns)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return false;

        // 检查是否匹配任一模式
        foreach (var pattern in patterns)
        {
            if (GlobMatcher.IsMatch(pattern, assemblyName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 检查类型是否应该被包含
    /// </summary>
    private bool ShouldIncludeType(Type type, List<string> patterns)
    {
        var typeNamespace = type.Namespace;
        if (string.IsNullOrEmpty(typeNamespace))
            return false;

        foreach (var pattern in patterns)
        {
            if (GlobMatcher.IsMatch(pattern, typeNamespace))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 检查是否是系统程序集
    /// </summary>
    private static bool IsSystemAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return true;

        return assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("PresentationCore", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("PresentationFramework", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取程序集的根命名空间
    /// </summary>
    private static string? GetRootNamespace(Assembly assembly)
    {
        // 优先从程序集中的类型获取公共命名空间前缀
        // 这比使用程序集名称更可靠，因为程序集名称可能与命名空间不同
        // 例如：程序集 "api-gateway" 的命名空间可能是 "api_gateway"
        try
        {
            var namespaces = assembly.GetTypes()
                .Where(t => !string.IsNullOrEmpty(t.Namespace) && t.IsPublic)
                .Select(t => t.Namespace!)
                .Distinct()
                .ToList();

            if (namespaces.Count > 0)
            {
                // 找到最短的公共命名空间前缀
                var commonPrefix = namespaces
                    .OrderBy(n => n.Length)
                    .First()
                    .Split('.')
                    .First();

                // 验证这个前缀确实是所有命名空间的公共前缀
                if (namespaces.All(n => n.StartsWith(commonPrefix, StringComparison.Ordinal)))
                {
                    return commonPrefix;
                }

                // 如果没有公共前缀，返回最短命名空间的第一部分
                return commonPrefix;
            }
        }
        catch
        {
            // 忽略反射异常
        }

        // 备选方案：使用程序集名称
        var assemblyName = assembly.GetName().Name;
        if (!string.IsNullOrEmpty(assemblyName))
            return assemblyName;

        return null;
    }

    /// <inheritdoc />
    public IEnumerable<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
    {
        return _scannedTypes.Where(t => t.GetCustomAttribute<TAttribute>() != null);
    }

    /// <inheritdoc />
    public IEnumerable<Type> GetTypesAssignableTo<TBase>()
    {
        return GetTypesAssignableTo(typeof(TBase));
    }

    /// <inheritdoc />
    public IEnumerable<Type> GetTypesAssignableTo(Type baseType)
    {
        return _scannedTypes.Where(t =>
            baseType.IsAssignableFrom(t) && t != baseType);
    }
}
