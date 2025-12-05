using System.Diagnostics;
using System.Reflection;
using Artisan.Attributes;
using Artisan.Configuration;
using Artisan.Core.Modules;
using Artisan.DependencyInjection;
using Artisan.Diagnostics;
using Artisan.Extensions;
using Artisan.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Artisan;

public class ArtisanApplicationV2
{
    /// <summary>
    /// 指定入口类型启动应用
    /// </summary>
    /// <param name="args">应用启动参数</param>
    /// <typeparam name="TEntry">用户应用类</typeparam>
    public static void Run<TEntry>(string[] args) where TEntry : class
    {
        new ArtisanApplicationV2(typeof(TEntry), args)
            .Boot()
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// 封装启动参数
    /// </summary>
    private class ArtisanBuildContext
    {
        public required Type EntryType { get; init; }

        public required IConfigurableApplication? AppInstance { get; init; }

        public required WebApplicationBuilder Builder { get; init; }

        public required ArtisanOptions Options { get; init; }

        public required ScanResult ScanResult { get; init; }

        public required IEnumerable<ArtisanModule> LoadedModules { get; init; }
    }

    private readonly ArtisanBuildContext _context;
    private readonly ILogger _logger;

    private ArtisanApplicationV2(Type entryType, string[] args)
    {
        _logger = InitializeLogger(args);
        _context = BuildContext(entryType, args);
    }

    private ILogger InitializeLogger(string[] args)
    {
        // 1. 初始化全局日志 (这里可以简单的解析一下 args 看看有没有 --debug)
        var logLevel = args.Contains("--debug") ? LogLevel.Debug : LogLevel.Information;
        ArtisanBootLogger.Initialize(logLevel);
        return ArtisanBootLogger.GetLogger<ArtisanApplicationV2>();
    }

    private ArtisanBuildContext BuildContext(Type entryType, string[] args)
    {
        // 初始化APP实例
        IConfigurableApplication? app = null;

        if (typeof(IConfigurableApplication).IsAssignableFrom(entryType))
        {
            try
            {
                // 只有实现了接口才实例化
                app = Activator.CreateInstance(entryType) as IConfigurableApplication;
            }
            catch (Exception ex)
            {
                // 用户可能忘了把构造函数设为 public
                throw new InvalidOperationException(
                    $"入口类 '{entryType.Name}' 实现了 IConfigurableApplication，但无法实例化。请确保它有一个无参的 public 构造函数。", ex);
            }
        }

        // 初始化扫描依赖
        var builder = WebApplication.CreateBuilder(args);
        var options = new ArtisanOptions();
        var scanResult = new AssemblyScanner().Scan(entryType);

        // 处理用户配置
        app?.ConfigureArtisan(options);
        var loader = new ModuleLoader(options.DisabledModules);
        var modules = loader.LoadModulesFromTypes(scanResult.Modules, builder.Configuration);
        
        // 清理容器内部的Logger依赖，防止内部日志无法打印的问题
        

        return new ArtisanBuildContext()
        {
            EntryType = entryType,
            AppInstance = app,
            Options = options,
            ScanResult = scanResult,
            Builder = builder,
            LoadedModules = modules
        };
    }

    /// <summary>
    /// 处理应用启动流程
    /// </summary>
    public async Task Boot()
    {
        _logger.LogInformation("Starting Artisan Application...");
        ProcessControllers();
        ProcessInjectables();
        ProcessConfigurations();
        ProcessConfigureServices();
        await RunApplication();
    }

    /// <summary>
    /// 处理控制器
    /// </summary>
    private void ProcessControllers()
    {
        _logger.LogInformation("Artisan is processing controllers...");
        var mvcBuilder = _context.Builder.Services.AddControllers();
        var asms = _context.ScanResult.ControllerAssemblies;

        foreach (var asm in asms)
        {
            mvcBuilder.AddApplicationPart(asm);
        }
    }

    /// <summary>
    /// 处理依赖
    /// </summary>
    private void ProcessInjectables()
    {
        _logger.LogInformation("Artisan is processing injectables...");
        ServiceRegistrar.RegisterTypes(
            _context.Builder.Services,
            _context.ScanResult.Injectables);
    }

    /// <summary>
    /// 处理配置项依赖
    /// </summary>
    private void ProcessConfigurations()
    {
        _logger.LogInformation("Artisan is processing configurations...");
        ConfigRegistrar.RegisterTypes(
            _context.Builder.Services,
            _context.Builder.Configuration,
            _context.ScanResult.Configurations);
    }

    /// <summary>
    /// 处理.net配置管道
    /// </summary>
    private void ProcessConfigureServices()
    {
        _logger.LogInformation("Artisan is processing web configurations...");
        foreach (var module in _context.LoadedModules)
        {
            module.ConfigureServices(_context.Builder.Services);
        }

        // 执行用户的 ConfigureServices (特权覆盖)
        _context.AppInstance?.ConfigureServices(_context.Builder.Services);
    }

    /// <summary>
    /// 运行App
    /// </summary>
    private async Task RunApplication()
    {
        _logger.LogInformation("Artisan is building and running web application...");
        var app = _context.Builder.Build();

        // 处理用户管道
        _context.AppInstance?.Configure(app);

        // 处理模块管道
        foreach (var module in _context.LoadedModules)
        {
            module.Configure(app);
        }

        _logger.LogInformation("Artisan finding API endpoints:");
        var endpoints = app.GetEndpoints();
        _logger.LogTable(endpoints, table =>
        {
            table
                .AddColumn("Method", x => x.ActionConstraints?
                    .OfType<HttpMethodActionConstraint>()
                    .FirstOrDefault()?.HttpMethods.First() ?? "ANY")
                .AddColumn("Route Pattern", x => x.AttributeRouteInfo?.Template ?? "N/A")
                .AddColumn("Controller", x => x.ControllerName)
                .AddColumn("Action", x => x.ActionName);
        });

        await app.RunAsync();
    }
}