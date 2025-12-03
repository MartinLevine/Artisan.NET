namespace Artisan.DependencyInjection;

/// <summary>
/// 属性注入器接口
/// </summary>
public interface IPropertyInjector
{
    /// <summary>
    /// 对实例执行属性注入
    /// </summary>
    /// <param name="instance">要注入的实例</param>
    void InjectProperties(object instance);

    /// <summary>
    /// 对实例执行属性注入（异步）
    /// </summary>
    /// <param name="instance">要注入的实例</param>
    Task InjectPropertiesAsync(object instance);
}
