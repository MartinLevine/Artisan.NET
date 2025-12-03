using Artisan.DependencyInjection;

namespace Artisan.Attributes;

/// <summary>
/// 标记一个类可以被注入到 DI 容器
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class InjectableAttribute : Attribute
{
    public string? Key { get; }
    public Lifetime Lifetime { get; set; } = Lifetime.Scoped;

    public InjectableAttribute(string? key = null)
    {
        Key = key;
    }
}
