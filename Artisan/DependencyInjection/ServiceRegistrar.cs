using System.Reflection;
using Artisan.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Artisan.DependencyInjection;

public static class ServiceRegistrar
{
    /// <summary>
    /// 注册扫描到的所有服务类型
    /// </summary>
    public static void RegisterTypes(IServiceCollection services, IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            // 双重检查：确保是类且不抽象 (虽然 Scanner 可能已经过滤过)
            if (!type.IsClass || type.IsAbstract) continue;

            var attr = type.GetCustomAttribute<InjectableAttribute>();
            if (attr == null) continue;

            RegisterType(services, type, attr);
        }
    }

    private static void RegisterType(IServiceCollection services, Type implementationType, InjectableAttribute attr)
    {
        var lifetime = ConvertLifetime(attr.Lifetime);
        var serviceKey = attr.Key; // 支持 Keyed Service

        // 1. 查找该类实现的所有有效接口
        // 过滤掉 System 接口 (IDisposable 等)
        var interfaces = implementationType.GetInterfaces()
            .Where(i => !IsSystemInterface(i))
            .ToArray();

        // 2. 处理开放泛型 (Open Generics)
        // 例如: public class Repository<T> : IRepository<T>
        if (implementationType.IsGenericTypeDefinition)
        {
            // 泛型无法使用 Factory 转发，只能直接注册 Interface -> Implementation
            if (serviceKey != null)
            {
                // Keyed Open Generic
                services.TryAddKeyed(
                    new ServiceDescriptor(implementationType, serviceKey, implementationType, lifetime));
                foreach (var i in interfaces)
                {
                    services.TryAddKeyed(new ServiceDescriptor(i, serviceKey, implementationType, lifetime));
                }
            }
            else
            {
                // Normal Open Generic
                services.TryAdd(new ServiceDescriptor(implementationType, implementationType, lifetime));
                foreach (var i in interfaces)
                {
                    services.TryAdd(new ServiceDescriptor(i, implementationType, lifetime));
                }
            }

            return;
        }

        // 3. 处理普通类型 (Closed Types)
        // 核心策略：先注册自身 (Implementation)，再把接口指向自身 (Forwarding)
        // 这样保证 Singleton 模式下，注入接口和注入自身拿到的是同一个实例

        // A. 注册自身: Service -> Service
        if (serviceKey != null)
        {
            services.TryAddKeyed(new ServiceDescriptor(implementationType, serviceKey, implementationType, lifetime));
        }
        else
        {
            services.TryAdd(new ServiceDescriptor(implementationType, implementationType, lifetime));
        }

        // B. 注册接口: IService -> Service (通过转发)
        foreach (var interfaceType in interfaces)
        {
            if (serviceKey != null)
            {
                // Keyed Service Forwarding
                services.TryAddKeyed(new ServiceDescriptor(
                    interfaceType,
                    serviceKey,
                    (sp, key) => sp.GetRequiredKeyedService(implementationType, key),
                    lifetime));
            }
            else
            {
                // Normal Service Forwarding
                services.TryAdd(new ServiceDescriptor(
                    interfaceType,
                    sp => sp.GetRequiredService(implementationType),
                    lifetime));
            }
        }
    }

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

    /// <summary>
    /// 过滤系统接口，避免把 IDisposable, ISerializable 等注册进 DI
    /// </summary>
    private static bool IsSystemInterface(Type type)
    {
        if (type.Namespace == null) return false;

        return type.Namespace.StartsWith("System")
               || type.Namespace.StartsWith("Microsoft");
    }

    /// <summary>
    /// 安全添加 Keyed Service (如果已存在 Type + Key 的组合则跳过)
    /// </summary>
    public static void TryAddKeyed(this IServiceCollection services, ServiceDescriptor descriptor)
    {
        // 1. 检查 descriptor 是否真的是 Keyed (以防万一)
        if (!descriptor.IsKeyedService)
        {
            services.TryAdd(descriptor);
            return;
        }

        // 2. 核心查重逻辑：检查是否存在 ServiceType 和 ServiceKey 都相同的描述符
        var exists = services.Any(d =>
            d.ServiceType == descriptor.ServiceType &&
            object.Equals(d.ServiceKey, descriptor.ServiceKey));

        if (!exists)
        {
            services.Add(descriptor);
        }
    }
}