using Artisan.DependencyInjection;

namespace Artisan.Attributes;

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