namespace Artisan.Configuration;

/// <summary>
/// 获取单个配置值
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class GetValueAttribute : Attribute
{
    public string Key { get; }
    public object? DefaultValue { get; set; }

    public GetValueAttribute(string key)
    {
        Key = key;
    }
}
