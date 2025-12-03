namespace Artisan.DependencyInjection;

/// <summary>
/// 服务生命周期
/// </summary>
public enum Lifetime
{
    /// <summary>
    /// 每次注入创建新实例
    /// </summary>
    Transient,

    /// <summary>
    /// 每个请求一个实例
    /// </summary>
    Scoped,

    /// <summary>
    /// 全局单例
    /// </summary>
    Singleton
}
