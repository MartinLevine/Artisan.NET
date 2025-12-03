using Artisan.DependencyInjection;

namespace Artisan.Attributes;

/// <summary>
/// 通用组件，默认 Singleton 生命周期
/// 适用于无状态的工具类、帮助类
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ComponentAttribute : InjectableAttribute
{
    /// <summary>
    /// 是否跟随 HTTP Session 生命周期
    /// 设为 true 时，每个用户会话拥有独立实例
    /// </summary>
    public bool SessionScoped { get; set; } = false;

    public ComponentAttribute(string? key = null) : base(key)
    {
        Lifetime = Lifetime.Singleton;
    }
}
