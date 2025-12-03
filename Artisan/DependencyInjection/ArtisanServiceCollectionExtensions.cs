using Artisan.AspNetCore;
using Artisan.Configuration;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.DependencyInjection;

/// <summary>
/// Artisan 服务集合扩展方法
/// </summary>
public static class ArtisanServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Artisan 服务（扫描并注册所有标记的类型）
    /// </summary>
    public static IServiceCollection AddArtisanServices(
        this IServiceCollection services,
        IAssemblyScanner scanner,
        IConfiguration configuration)
    {
        // 注册配置系统
        ConfigurationRegistrar.RegisterAppSettings(services, configuration, scanner.ScannedTypes);

        // 注册所有服务
        ServiceRegistrar.RegisterServices(services, scanner.ScannedTypes, configuration);

        // 注册 AssemblyScanner 作为单例
        services.AddSingleton<IAssemblyScanner>(scanner);

        // 注册属性注入器
        services.AddPropertyInjector();

        // 注册自定义 Controller 激活器以支持属性注入
        services.AddSingleton<IControllerActivator, ArtisanControllerActivator>();

        return services;
    }
}
