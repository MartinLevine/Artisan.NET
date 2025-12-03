namespace Artisan.Configuration;

/// <summary>
/// 配置访问接口（可选，用于动态获取配置）
/// </summary>
public interface IAppSettings
{
    /// <summary>
    /// 获取配置值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">配置键（支持冒号分隔的路径，如 "App:Name"）</param>
    /// <returns>配置值，不存在时返回 null</returns>
    T? GetValue<T>(string key);

    /// <summary>
    /// 获取配置值，带默认值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">配置键</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>配置值</returns>
    T GetValue<T>(string key, T defaultValue);

    /// <summary>
    /// 获取配置节并绑定到对象
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="section">配置节名称</param>
    /// <returns>绑定后的对象</returns>
    T? GetSection<T>(string section) where T : class, new();

    /// <summary>
    /// 检查配置键是否存在
    /// </summary>
    /// <param name="key">配置键</param>
    /// <returns>是否存在</returns>
    bool Exists(string key);
}
