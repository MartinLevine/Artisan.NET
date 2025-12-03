using System.Reflection;
using Artisan.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Artisan.Configuration;

public static class ConfigRegistrar
{
    /// <summary>
    /// 注册带有 [AppSetting] 的配置类
    /// </summary>
    public static void RegisterTypes(IServiceCollection services, IConfiguration configuration, IEnumerable<Type> types)
    {
        // 缓存反射所需的 Open Generic MethodInfo，避免在循环中重复获取
        var configureMethodInfo = typeof(OptionsConfigurationServiceCollectionExtensions)
                                      .GetMethod(nameof(OptionsConfigurationServiceCollectionExtensions.Configure),
                                          new[] { typeof(IServiceCollection), typeof(IConfiguration) })
                                  ?? throw new InvalidOperationException(
                                      "Cannot find IServiceCollection.Configure method.");

        foreach (var type in types)
        {
            if (type.IsAbstract || !type.IsClass) continue;

            var attr = type.GetCustomAttribute<ConfigurationAttribute>();
            if (attr == null) continue;

            // 1. 获取配置节
            var section = configuration.GetSection(attr.Section);

            // 2. 利用反射调用 services.Configure<T>(section)
            // 这步操作注册了 IOptions<T>, IOptionsSnapshot<T>, IOptionsMonitor<T>
            var genericConfigure = configureMethodInfo.MakeGenericMethod(type);
            genericConfigure.Invoke(null, new object[] { services, section });

            // 3. 注册 POCO 对象本身 (Singleton)
            // 这是一个 Factory 注册：当用户请求 T 时，我们从 IOptions<T> 中取值
            // 这样既保持了 POCO 的简洁，又底层连接到了 Options 系统
            services.AddSingleton(type, sp => GetOptionsValue(sp, type));
        }
    }

    /// <summary>
    /// 辅助方法：运行时动态获取 IOptions<T>.Value
    /// </summary>
    private static object GetOptionsValue(IServiceProvider sp, Type configType)
    {
        // 构造 IOptions<T> 类型
        var optionsType = typeof(IOptions<>).MakeGenericType(configType);

        // 从容器获取 IOptions<T> 服务
        var optionsService = sp.GetRequiredService(optionsType);

        // 反射获取 .Value 属性
        var valueProp = optionsType.GetProperty(nameof(IOptions<object>.Value));

        return valueProp!.GetValue(optionsService)!;
    }
}