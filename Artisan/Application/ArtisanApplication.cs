using System.Diagnostics;
using System.Reflection;
using Artisan.Attributes;
using Artisan.Configuration;
using Artisan.DependencyInjection;
using Artisan.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.Application;

/// <summary>
/// Artisan 应用启动器
/// 实现三阶段启动流程：ConfigureArtisan -> ConfigureServices -> Configure
/// </summary>
public static class ArtisanApplication
{
    /// <summary>
    /// 启动应用（自动发现模块，无需指定启动模块）
    /// </summary>
    public static void Run(string[] args)
    {
        RunAsync(args).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步启动应用
    /// </summary>
    public static async Task RunAsync(string[] args)
    {
        // 1. 从调用栈获取入口类（标记了 [ArtisanApplication] 的类）
        var entryType = FindEntryType();

        // 2. 验证 [ArtisanApplication] 特性
        var appAttr = entryType.GetCustomAttribute<ArtisanApplicationAttribute>()
                      ?? throw new InvalidOperationException(
                          $"{entryType.Name} must have [ArtisanApplication] attribute");

        // 3. 检查是否实现了 IConfigurableApplication 接口
        IConfigurableApplication? configurableApp = null;
        if (typeof(IConfigurableApplication).IsAssignableFrom(entryType))
        {
            configurableApp = (IConfigurableApplication)Activator.CreateInstance(entryType)!;
        }

        // ==========================================
        // 阶段 1：框架预配置（最先执行）
        // ==========================================
        var artisanOptions = new ArtisanOptions();
        configurableApp?.ConfigureArtisan(artisanOptions);

        // 4. 构建扫描模式列表
        var scanPatterns = BuildScanPatterns(entryType, appAttr);

        // 5. 构建 WebApplication
        var builder = WebApplication.CreateBuilder(args);

        // 6. 扫描程序集
        var entryAssembly = entryType.Assembly;
        var scanner = new AssemblyScanner();
        scanner.Scan(entryAssembly, scanPatterns);

        // 7. 注册服务和配置
        builder.Services.AddArtisanServices(scanner, builder.Configuration);

        // 8. 自动发现所有模块并解析依赖链（基于程序集引用推导）
        var moduleLoader = new ModuleLoader();
        var modules = moduleLoader.LoadModules(
            entryAssembly,
            builder.Services,
            builder.Configuration,
            artisanOptions);

        // ==========================================
        // 阶段 2：服务注册（中间执行）
        // ==========================================

        // 9. 按依赖顺序调用模块的 ConfigureServices
        foreach (var module in modules)
        {
            module.ConfigureServices(builder.Services);
        }

        // 10. 调用用户 Application 的 ConfigureServices（最后执行，可覆盖模块配置）
        configurableApp?.ConfigureServices(builder.Services);

        var app = builder.Build();

        // ==========================================
        // 阶段 3：中间件管道（最后执行）
        // ==========================================

        // 11. 按依赖顺序调用模块的 Configure
        foreach (var module in modules)
        {
            module.Configure(app);
        }

        // 12. 调用用户 Application 的 Configure（最后执行，可覆盖模块配置）
        configurableApp?.Configure(app);

        await app.RunAsync();
    }

    /// <summary>
    /// 创建 WebApplicationBuilder（用于需要更多控制的场景）
    /// </summary>
    public static ArtisanApplicationBuilder CreateBuilder(string[] args)
    {
        return new ArtisanApplicationBuilder(args);
    }

    private static string[] BuildScanPatterns(Type entryType, ArtisanApplicationAttribute appAttr)
    {
        var patterns = new List<string>();

        // 自动添加应用类所在命名空间
        var baseNamespace = entryType.Namespace;
        if (!string.IsNullOrEmpty(baseNamespace))
        {
            patterns.Add(appAttr.ScanSubNamespaces ? $"{baseNamespace}.**" : baseNamespace);
        }

        // 添加额外的 [ScanAssembly] 模式
        var extraPatterns = entryType.GetCustomAttributes<ScanAssemblyAttribute>()
            .Select(a => a.Pattern);
        patterns.AddRange(extraPatterns);

        return patterns.ToArray();
    }

    private static Type FindEntryType()
    {
        var stackTrace = new StackTrace();
        foreach (var frame in stackTrace.GetFrames())
        {
            var method = frame.GetMethod();
            if (method?.Name == "Main" && method.DeclaringType != null)
            {
                if (method.DeclaringType.GetCustomAttribute<ArtisanApplicationAttribute>() != null)
                {
                    return method.DeclaringType;
                }
            }
        }

        // 备选方案：查找入口程序集中的 ArtisanApplication 类
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            var entryType = entryAssembly.GetTypes()
                .FirstOrDefault(t => t.GetCustomAttribute<ArtisanApplicationAttribute>() != null);
            if (entryType != null)
                return entryType;
        }

        throw new InvalidOperationException(
            "Cannot find entry type with [ArtisanApplication] attribute. " +
            "Make sure your application class is decorated with [ArtisanApplication].");
    }
}

/// <summary>
/// Artisan 应用构建器（提供更细粒度的控制）
/// </summary>
public class ArtisanApplicationBuilder
{
    private readonly string[] _args;
    private readonly List<string> _additionalPatterns = new();
    private Type? _entryType;
    private ArtisanOptions _options = new();

    internal ArtisanApplicationBuilder(string[] args)
    {
        _args = args;
    }

    /// <summary>
    /// 指定入口类型
    /// </summary>
    public ArtisanApplicationBuilder UseEntryType<T>()
    {
        _entryType = typeof(T);
        return this;
    }

    /// <summary>
    /// 添加额外的扫描模式
    /// </summary>
    public ArtisanApplicationBuilder AddScanPattern(string pattern)
    {
        _additionalPatterns.Add(pattern);
        return this;
    }

    /// <summary>
    /// 配置 Artisan 选项
    /// </summary>
    public ArtisanApplicationBuilder ConfigureOptions(Action<ArtisanOptions> configure)
    {
        configure(_options);
        return this;
    }

    /// <summary>
    /// 构建并运行应用
    /// </summary>
    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步构建并运行应用
    /// </summary>
    public async Task RunAsync()
    {
        var entryType = _entryType ?? FindEntryType();
        var appAttr = entryType.GetCustomAttribute<ArtisanApplicationAttribute>()
                      ?? new ArtisanApplicationAttribute();

        IConfigurableApplication? configurableApp = null;
        if (typeof(IConfigurableApplication).IsAssignableFrom(entryType))
        {
            configurableApp = (IConfigurableApplication)Activator.CreateInstance(entryType)!;
        }

        // 阶段 1
        configurableApp?.ConfigureArtisan(_options);

        var patterns = BuildScanPatterns(entryType, appAttr);

        var builder = WebApplication.CreateBuilder(_args);

        var entryAssembly = entryType.Assembly;
        var scanner = new AssemblyScanner();
        scanner.Scan(entryAssembly, patterns);

        builder.Services.AddArtisanServices(scanner, builder.Configuration);

        var moduleLoader = new ModuleLoader();
        var modules = moduleLoader.LoadModules(
            entryAssembly,
            builder.Services,
            builder.Configuration,
            _options);

        // 阶段 2
        foreach (var module in modules)
        {
            module.ConfigureServices(builder.Services);
        }

        configurableApp?.ConfigureServices(builder.Services);

        var app = builder.Build();

        // 阶段 3
        foreach (var module in modules)
        {
            module.Configure(app);
        }

        configurableApp?.Configure(app);

        await app.RunAsync();
    }

    private List<string> BuildScanPatterns(Type entryType, ArtisanApplicationAttribute appAttr)
    {
        var patterns = new List<string>();

        var baseNamespace = entryType.Namespace;
        if (!string.IsNullOrEmpty(baseNamespace))
        {
            patterns.Add(appAttr.ScanSubNamespaces ? $"{baseNamespace}.**" : baseNamespace);
        }

        var extraPatterns = entryType.GetCustomAttributes<ScanAssemblyAttribute>()
            .Select(a => a.Pattern);
        patterns.AddRange(extraPatterns);
        patterns.AddRange(_additionalPatterns);

        return patterns;
    }

    private static Type FindEntryType()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            var entryType = entryAssembly.GetTypes()
                .FirstOrDefault(t => t.GetCustomAttribute<ArtisanApplicationAttribute>() != null);
            if (entryType != null)
                return entryType;
        }

        throw new InvalidOperationException(
            "Cannot find entry type with [ArtisanApplication] attribute.");
    }
}
