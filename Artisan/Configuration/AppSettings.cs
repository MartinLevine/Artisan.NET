using Microsoft.Extensions.Configuration;

namespace Artisan.Configuration;

/// <summary>
/// 配置访问实现
/// </summary>
public class AppSettings : IAppSettings
{
    private readonly IConfiguration _configuration;

    public AppSettings(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public T? GetValue<T>(string key)
    {
        return _configuration.GetValue<T>(key);
    }

    /// <inheritdoc />
    public T GetValue<T>(string key, T defaultValue)
    {
        return _configuration.GetValue(key, defaultValue) ?? defaultValue;
    }

    /// <inheritdoc />
    public T? GetSection<T>(string section) where T : class, new()
    {
        var configSection = _configuration.GetSection(section);
        if (!configSection.Exists())
            return null;

        var instance = new T();
        configSection.Bind(instance);
        return instance;
    }

    /// <inheritdoc />
    public bool Exists(string key)
    {
        return _configuration.GetSection(key).Exists();
    }
}
