namespace Artisan.Configuration;

/// <summary>
/// 将配置节映射到类，或标记属性从指定配置节注入
/// 底层使用 IOptions<T> / IOptionsMonitor<T> 实现
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class AppSettingAttribute : Attribute
{
    public string Section { get; }

    /// <summary>
    /// 是否启用热更新（默认 false，使用 IOptions）
    /// 设为 true 时使用 IOptionsMonitor，配置文件变更后自动更新
    /// </summary>
    public bool HotReload { get; set; } = false;

    public AppSettingAttribute(string section)
    {
        Section = section;
    }
}
