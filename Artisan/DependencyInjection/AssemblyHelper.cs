using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Artisan.DependencyInjection;

public static class AssemblyHelper
{
    public static IEnumerable<Assembly> LoadFromPattern(string pattern)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

        // 智能处理扩展名：如果用户只写了 "MyApp.*"，自动补全为 "MyApp.*.dll"
        // 这样用户在用的时候就不需要关心 .dll 后缀了
        if (!pattern.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            pattern += ".dll";
        }

        matcher.AddInclude(pattern);

        // 在 BaseDirectory 下执行搜索
        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(baseDir)));

        if (!result.HasMatches)
        {
            return [];
        }

        var loadedAssemblies = new List<Assembly>();

        foreach (var file in result.Files)
        {
            var filePath = Path.Combine(baseDir, file.Path);
            var assembly = LoadAssembly(filePath);
            
            if (assembly != null)
            {
                loadedAssemblies.Add(assembly);
            }
        }

        return loadedAssemblies;
    }

    /// <summary>
    /// 安全加载 (避免重复加载)
    /// </summary>
    private static Assembly? LoadAssembly(string filePath)
    {
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(filePath);

            // 1. 检查内存中是否已存在
            var existing = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), assemblyName));

            if (existing != null) return existing;

            // 2. 加载到默认上下文
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(filePath);
        }
        catch (BadImageFormatException)
        {
            return null; // 忽略非托管 DLL
        }
        catch (Exception)
        {
            return null; // 忽略其他错误
        }
    }
}