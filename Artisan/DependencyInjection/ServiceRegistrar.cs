using System.Reflection;
using Artisan.Attributes;
using Artisan.Configuration;
using Artisan.Session;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.DependencyInjection;

/// <summary>
/// 服务注册器
/// 负责扫描并注册所有标记了 DI 相关特性的类型
/// </summary>
public static class ServiceRegistrar
{
    /// <summary>
    /// 注册所有扫描到的服务
    /// </summary>
    public static void RegisterServices(
        IServiceCollection services,
        IEnumerable<Type> scannedTypes,
        IConfiguration configuration)
    {
        // 检查是否需要添加 Session 支持
        var hasSessionScoped = scannedTypes.Any(t =>
            t.GetCustomAttribute<ComponentAttribute>()?.SessionScoped == true);

        if (hasSessionScoped)
        {
            services.AddSessionLifetime();
        }

        foreach (var type in scannedTypes)
        {
            // 注册 [Injectable], [Service], [Repository], [Component] 标记的类
            var injectableAttr = type.GetCustomAttribute<InjectableAttribute>();
            if (injectableAttr != null)
            {
                // 检查是否是 Session 作用域
                var componentAttr = type.GetCustomAttribute<ComponentAttribute>();
                if (componentAttr?.SessionScoped == true)
                {
                    RegisterSessionScopedType(services, type);
                }
                else
                {
                    RegisterInjectableType(services, type, injectableAttr);
                }
            }

            // 注册 [DynamicInjectable] 标记的方法
            RegisterDynamicInjectables(services, type, configuration);
        }
    }

    /// <summary>
    /// 注册 Session 作用域类型
    /// </summary>
    private static void RegisterSessionScopedType(
        IServiceCollection services,
        Type implementationType)
    {
        var interfaces = implementationType.GetInterfaces()
            .Where(i => !i.Namespace?.StartsWith("System") == true)
            .ToList();

        if (interfaces.Any())
        {
            foreach (var iface in interfaces)
            {
                SessionServiceRegistrar.RegisterSessionService(services, iface, implementationType);
            }
        }

        // 同时注册实现类本身
        SessionServiceRegistrar.RegisterSessionService(services, implementationType, implementationType);
    }

    /// <summary>
    /// 注册可注入类型
    /// </summary>
    private static void RegisterInjectableType(
        IServiceCollection services,
        Type implementationType,
        InjectableAttribute attribute)
    {
        var lifetime = ConvertLifetime(attribute.Lifetime);
        var interfaces = implementationType.GetInterfaces()
            .Where(i => !i.Namespace?.StartsWith("System") == true)
            .ToList();

        if (attribute.Key != null)
        {
            // Keyed 服务注册
            if (interfaces.Any())
            {
                foreach (var iface in interfaces)
                {
                    services.Add(new ServiceDescriptor(
                        iface,
                        attribute.Key,
                        implementationType,
                        lifetime));
                }
            }
            else
            {
                services.Add(new ServiceDescriptor(
                    implementationType,
                    attribute.Key,
                    implementationType,
                    lifetime));
            }
        }
        else
        {
            // 普通服务注册
            if (interfaces.Any())
            {
                foreach (var iface in interfaces)
                {
                    services.Add(new ServiceDescriptor(iface, implementationType, lifetime));
                }
            }

            // 同时注册实现类本身
            services.Add(new ServiceDescriptor(implementationType, implementationType, lifetime));
        }
    }

    /// <summary>
    /// 注册动态可注入方法
    /// </summary>
    private static void RegisterDynamicInjectables(
        IServiceCollection services,
        Type type,
        IConfiguration configuration)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<DynamicInjectableAttribute>() != null);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<DynamicInjectableAttribute>()!;
            var returnType = method.ReturnType;
            var lifetime = ConvertLifetime(attr.Lifetime);

            // 创建工厂函数
            Func<IServiceProvider, object> factory = sp =>
            {
                // 获取包含该方法的实例
                var instance = ActivatorUtilities.CreateInstance(sp, type);

                // 调用方法获取返回值
                return method.Invoke(instance, null)!;
            };

            if (attr.Key != null)
            {
                // Keyed 服务
                services.Add(new ServiceDescriptor(returnType, attr.Key, (sp, key) => factory(sp), lifetime));
            }
            else
            {
                services.Add(new ServiceDescriptor(returnType, factory, lifetime));
            }
        }
    }

    /// <summary>
    /// 转换生命周期枚举
    /// </summary>
    private static ServiceLifetime ConvertLifetime(Lifetime lifetime)
    {
        return lifetime switch
        {
            Lifetime.Transient => ServiceLifetime.Transient,
            Lifetime.Scoped => ServiceLifetime.Scoped,
            Lifetime.Singleton => ServiceLifetime.Singleton,
            _ => ServiceLifetime.Scoped
        };
    }
}
