using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.Session;

/// <summary>
/// Session 服务注册器 - 用于注册 Session 作用域服务到 DI
/// </summary>
public static class SessionServiceRegistrar
{
    /// <summary>
    /// 注册 Session 作用域服务
    /// </summary>
    public static void RegisterSessionService(
        IServiceCollection services,
        Type serviceType,
        Type implementationType)
    {
        services.AddScoped(serviceType, sp =>
        {
            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            var sessionFactory = sp.GetRequiredService<ISessionServiceFactory>();

            var context = httpContextAccessor.HttpContext
                          ?? throw new InvalidOperationException(
                              "Session-scoped services require an active HTTP context");

            var sessionId = context.Session.Id;

            return sessionFactory.GetOrCreate(
                implementationType,
                sessionId,
                serviceProvider => ActivatorUtilities.CreateInstance(serviceProvider, implementationType),
                sp
            );
        });
    }

    /// <summary>
    /// 添加 Session 生命周期服务到 DI 容器
    /// </summary>
    public static IServiceCollection AddSessionLifetime(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ISessionServiceFactory, SessionServiceFactory>();
        services.AddScoped<SessionLifetimeMiddleware>();
        return services;
    }
}
