using System.Diagnostics;
using System.Reflection;
using Artisan.Attributes;
using Artisan.Configuration;
using Artisan.Core.Modules;
using Artisan.DependencyInjection;
using Artisan.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Artisan;

public class ArtisanApplication
{
    private readonly string[] _args;
    private Type _entryType;
    private IConfigurableApplication? _appInstance;
    private ArtisanOptions _options;
    private WebApplicationBuilder _builder;
    private ScanResult _scanResult;
    private IEnumerable<ArtisanModule> _modules;
    private ILogger _bootLogger;
    
    private ArtisanApplication(string[] args)
    {
        _args = args;
        _options = new ArtisanOptions();
        _modules = new List<ArtisanModule>();
        // 初始化为空，稍后填充
        _entryType = null!; 
        _builder = null!;
        _scanResult = null!;
    }
    
    // === 3. 静态入口 (对外 API 保持不变) ===
    public static void Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    public static async Task RunAsync(string[] args)
    {
        // 这里就是你说的：实例化 -> 执行
        var bootstrapper = new ArtisanApplication(args);
        await bootstrapper.StartAsync();
    }
    
    // === 4. 核心启动流程 (流水线) ===
    private async Task StartAsync()
    {
        // 步骤 1: 准备环境 (找到入口，实例化用户类，加载配置)
        PrepareEnvironment();

        // 步骤 2: 扫描程序集 (一次性扫描所有)
        ScanAssemblies();

        // 步骤 3: 初始化 Builder
        CreateBuilder();

        // 步骤 4: 加载模块 & 注册服务 (DI 阶段)
        RegisterServices();

        // 步骤 5: 构建应用
        var app = _builder.Build();

        // 步骤 6: 配置管道 (Middleware 阶段)
        ConfigurePipeline(app);

        // 步骤 7: 启动
        await app.RunAsync();
    }
    
    // === 5. 各个步骤的具体实现 (拆得更细了) ===

    private void PrepareEnvironment()
    {
        // 1. 找入口
        _entryType = FindEntryType();
        
        // 2. 实例化用户的 Application 类 (如果有的话)
        if (typeof(IConfigurableApplication).IsAssignableFrom(_entryType))
        {
            _appInstance = Activator.CreateInstance(_entryType) as IConfigurableApplication;
        }

        // 3. 执行阶段 1 配置 (Pre-Config)
        _appInstance?.ConfigureArtisan(_options);
    }
    
    private void ScanAssemblies()
    {
        // 使用之前设计的单次扫描器
        var scanner = new AssemblyScanner();
        // 如果有 ScanAssembly 特性，可以从 _entryType 上获取并传给 Scanner
        _scanResult = scanner.Scan(_entryType);
    }

    private void CreateBuilder()
    {
        _builder = WebApplication.CreateBuilder(_args);
    }
    
    private void RegisterServices()
    {
        // 1. 处理 MVC Controllers
        var mvcBuilder = _builder.Services.AddControllers();
        foreach (var asm in _scanResult.ControllerAssemblies)
        {
            mvcBuilder.AddApplicationPart(asm);
        }

        // 2. 加载模块 (逻辑)
        var loader = new ModuleLoader(_options.DisabledModules);
        _modules = loader.LoadModulesFromTypes(_scanResult.Modules, _builder.Configuration);

        // 3. 注册扫描到的 Services/Repositories
        ServiceRegistrar.RegisterTypes(_builder.Services, _scanResult.Injectables);
        
        // 4. 注册 Configuration
        ConfigRegistrar.RegisterTypes(_builder.Services, _builder.Configuration, _scanResult.Configurations);

        // 5. 执行模块的 ConfigureServices
        foreach (var module in _modules)
        {
            module.ConfigureServices(_builder.Services);
        }

        // 6. 执行用户的 ConfigureServices (特权覆盖)
        _appInstance?.ConfigureServices(_builder.Services);
    }
    
    private void ConfigurePipeline(WebApplication app)
    {
        // 1. 用户自定义管道 (最优先)
        _appInstance?.Configure(app);

        // 2. 模块管道
        foreach (var module in _modules)
        {
            module.Configure(app);
        }
    }

    private Type FindEntryType()
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