using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Artisan.Diagnostics;

/// <summary>
/// Artisan 引导期日志管理器 (全局静态单例)
/// 允许框架内部组件和第三方模块在 DI 容器就绪前记录日志
/// </summary>
public static class ArtisanBootLogger
{
    private static ILoggerFactory? _factory;

    /// <summary>
    /// 获取底层的 ILoggerFactory (如果需要高级操作)
    /// </summary>
    private static ILoggerFactory Factory => _factory ??= new NullLoggerFactory();

    /// <summary>
    /// 初始化日志系统 (仅限框架入口调用)
    /// </summary>
    internal static void Initialize(LogLevel minLevel)
    {
        if (_factory != null) return;

        _factory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = false;
                options.SingleLine = true;
                options.TimestampFormat = "[HH:mm:ss] ";
                options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
            });
            builder.SetMinimumLevel(minLevel);
        });
    }

    /// <summary>
    /// 获取指定类型的 Logger
    /// </summary>
    public static ILogger GetLogger<T>()
    {
        return Factory.CreateLogger(typeof(T).Name);
    }

    /// <summary>
    /// 获取指定名称的 Logger
    /// </summary>
    public static ILogger GetLogger(string categoryName)
    {
        return Factory.CreateLogger(categoryName);
    }
}