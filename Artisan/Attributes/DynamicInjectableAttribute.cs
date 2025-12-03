using Artisan.DependencyInjection;

namespace Artisan.Attributes;

/// <summary>
/// 标记一个方法为动态依赖提供者（类似 Spring @Bean）
/// 方法可以放在任何被扫描到的类中
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class DynamicInjectableAttribute : Attribute
{
    public string? Key { get; }
    public Lifetime Lifetime { get; set; } = Lifetime.Singleton;

    public DynamicInjectableAttribute(string? key = null)
    {
        Key = key;
    }
}
