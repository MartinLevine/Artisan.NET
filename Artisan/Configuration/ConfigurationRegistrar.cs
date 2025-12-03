using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Artisan.Configuration;

/// <summary>
/// 配置注册器
/// 负责扫描并注册所有 [AppSetting] 标记的配置类
/// </summary>
public static class ConfigurationRegistrar
{
    /// <summary>
    /// 注册所有 AppSetting 配置类到 DI 容器
    /// </summary>
    public static void RegisterAppSettings(
        IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Type> scannedTypes)
    {
        // 注册 IAppSettings
        services.AddSingleton<IAppSettings, AppSettings>();

        foreach (var type in scannedTypes)
        {
            var appSettingAttr = type.GetCustomAttribute<AppSettingAttribute>();
            if (appSettingAttr == null)
                continue;

            // 使用 IOptions<T> 机制注册配置类
            RegisterConfigurationType(services, configuration, type, appSettingAttr);
        }
    }

    /// <summary>
    /// 注册单个配置类型
    /// </summary>
    private static void RegisterConfigurationType(
        IServiceCollection services,
        IConfiguration configuration,
        Type configType,
        AppSettingAttribute attribute)
    {
        var section = configuration.GetSection(attribute.Section);

        // 使用反射调用泛型方法 Configure<T>
        var configureMethod = typeof(OptionsConfigurationServiceCollectionExtensions)
            .GetMethods()
            .First(m => m.Name == "Configure" &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[1].ParameterType == typeof(IConfiguration));

        var genericMethod = configureMethod.MakeGenericMethod(configType);
        genericMethod.Invoke(null, new object[] { services, section });

        // 同时注册直接注入的支持（通过 IOptions<T>.Value）
        // 这样用户可以直接注入配置类而不是 IOptions<T>
        var serviceDescriptor = new ServiceDescriptor(
            configType,
            sp =>
            {
                var optionsType = typeof(IOptions<>).MakeGenericType(configType);
                var options = sp.GetRequiredService(optionsType);
                var valueProperty = optionsType.GetProperty("Value");
                return valueProperty!.GetValue(options)!;
            },
            ServiceLifetime.Singleton);

        services.Add(serviceDescriptor);
    }

    /// <summary>
    /// 为临时 ServiceProvider 注册配置类（用于模块实例化）
    /// </summary>
    public static void RegisterAppSettingsForTempProvider(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    var appSettingAttr = type.GetCustomAttribute<AppSettingAttribute>();
                    if (appSettingAttr != null)
                    {
                        var section = configuration.GetSection(appSettingAttr.Section);
                        var instance = section.Get(type) ?? Activator.CreateInstance(type);
                        if (instance != null)
                        {
                            services.AddSingleton(type, instance);
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // 忽略无法加载的程序集
            }
        }
    }
}
