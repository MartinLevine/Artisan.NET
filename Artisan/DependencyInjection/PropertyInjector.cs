using System.Reflection;
using Artisan.Attributes;
using Artisan.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.DependencyInjection;

/// <summary>
/// 属性注入器实现
/// 负责处理 [Inject]、[AppSetting] 和 [GetValue] 标记的属性注入
/// </summary>
public class PropertyInjector : IPropertyInjector
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public PropertyInjector(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public void InjectProperties(object instance)
    {
        var type = instance.GetType();
        InjectDependencies(instance, type);
        InjectAppSettings(instance, type);
        InjectConfigValues(instance, type);
    }

    /// <inheritdoc />
    public Task InjectPropertiesAsync(object instance)
    {
        InjectProperties(instance);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注入 [Inject] 标记的属性
    /// </summary>
    private void InjectDependencies(object instance, Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<InjectAttribute>() != null && p.CanWrite);

        foreach (var property in properties)
        {
            var injectAttr = property.GetCustomAttribute<InjectAttribute>()!;
            object? service;

            if (injectAttr.Key != null)
            {
                // Keyed 服务
                service = _serviceProvider.GetKeyedService<object>(injectAttr.Key);
                if (service != null && !property.PropertyType.IsInstanceOfType(service))
                {
                    service = null;
                }
            }
            else
            {
                service = _serviceProvider.GetService(property.PropertyType);
            }

            if (service == null && injectAttr.Required)
            {
                throw new InvalidOperationException(
                    $"Required service {property.PropertyType.Name} not found for property {type.Name}.{property.Name}");
            }

            if (service != null)
            {
                property.SetValue(instance, service);
            }
        }

        // 处理字段
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<InjectAttribute>() != null);

        foreach (var field in fields)
        {
            var injectAttr = field.GetCustomAttribute<InjectAttribute>()!;
            object? service;

            if (injectAttr.Key != null)
            {
                service = _serviceProvider.GetKeyedService<object>(injectAttr.Key);
                if (service != null && !field.FieldType.IsInstanceOfType(service))
                {
                    service = null;
                }
            }
            else
            {
                service = _serviceProvider.GetService(field.FieldType);
            }

            if (service == null && injectAttr.Required)
            {
                throw new InvalidOperationException(
                    $"Required service {field.FieldType.Name} not found for field {type.Name}.{field.Name}");
            }

            if (service != null)
            {
                field.SetValue(instance, service);
            }
        }
    }

    /// <summary>
    /// 注入 [AppSetting] 标记的属性
    /// </summary>
    private void InjectAppSettings(object instance, Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<AppSettingAttribute>() != null && p.CanWrite);

        foreach (var property in properties)
        {
            var attr = property.GetCustomAttribute<AppSettingAttribute>()!;
            var section = _configuration.GetSection(attr.Section);

            if (section.Exists())
            {
                var value = section.Get(property.PropertyType);
                if (value != null)
                {
                    property.SetValue(instance, value);
                }
            }
        }
    }

    /// <summary>
    /// 注入 [GetValue] 标记的属性
    /// </summary>
    private void InjectConfigValues(object instance, Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<GetValueAttribute>() != null && p.CanWrite);

        foreach (var property in properties)
        {
            var attr = property.GetCustomAttribute<GetValueAttribute>()!;
            var value = _configuration.GetValue(property.PropertyType, attr.Key);

            if (value != null)
            {
                property.SetValue(instance, value);
            }
            else if (attr.DefaultValue != null)
            {
                property.SetValue(instance, Convert.ChangeType(attr.DefaultValue, property.PropertyType));
            }
        }

        // 处理字段
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<GetValueAttribute>() != null);

        foreach (var field in fields)
        {
            var attr = field.GetCustomAttribute<GetValueAttribute>()!;
            var value = _configuration.GetValue(field.FieldType, attr.Key);

            if (value != null)
            {
                field.SetValue(instance, value);
            }
            else if (attr.DefaultValue != null)
            {
                field.SetValue(instance, Convert.ChangeType(attr.DefaultValue, field.FieldType));
            }
        }
    }
}

/// <summary>
/// 属性注入扩展
/// </summary>
public static class PropertyInjectorExtensions
{
    /// <summary>
    /// 添加属性注入器到服务集合
    /// </summary>
    public static IServiceCollection AddPropertyInjector(this IServiceCollection services)
    {
        services.AddSingleton<IPropertyInjector, PropertyInjector>();
        return services;
    }
}
