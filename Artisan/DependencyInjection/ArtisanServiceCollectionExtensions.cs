using Artisan.Configuration;
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

        return services;
    }
}
