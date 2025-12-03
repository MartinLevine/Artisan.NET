using Artisan.DependencyInjection;

namespace Artisan.Attributes;

/// <summary>
/// 服务层组件，默认 Scoped 生命周期
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ServiceAttribute : InjectableAttribute
{
    public ServiceAttribute(string? key = null) : base(key)
    {
        Lifetime = Lifetime.Scoped;
    }
}
