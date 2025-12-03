namespace Artisan.Attributes;

/// <summary>
/// 注入依赖到属性或字段
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class InjectAttribute : Attribute
{
    public string? Key { get; }
    public bool Required { get; set; } = true;

    public InjectAttribute(string? key = null)
    {
        Key = key;
    }
}
