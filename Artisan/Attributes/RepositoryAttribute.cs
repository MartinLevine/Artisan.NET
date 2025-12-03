using Artisan.DependencyInjection;

namespace Artisan.Attributes;

/// <summary>
/// 数据访问层组件，默认 Scoped 生命周期
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RepositoryAttribute : InjectableAttribute
{
    public RepositoryAttribute(string? key = null) : base(key)
    {
        Lifetime = Lifetime.Scoped;
    }
}
