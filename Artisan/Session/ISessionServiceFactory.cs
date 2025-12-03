namespace Artisan.Session;

/// <summary>
/// Session 作用域服务工厂
/// 生命周期与 HTTP Session 绑定
/// </summary>
public interface ISessionServiceFactory
{
    /// <summary>
    /// 获取或创建 Session 作用域的服务实例
    /// </summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <param name="sessionId">Session ID</param>
    /// <param name="factory">创建服务的工厂方法</param>
    /// <param name="serviceProvider">服务提供者</param>
    /// <returns>服务实例</returns>
    T GetOrCreate<T>(string sessionId, Func<IServiceProvider, T> factory, IServiceProvider serviceProvider)
        where T : class;

    /// <summary>
    /// 获取或创建 Session 作用域的服务实例（非泛型版本）
    /// </summary>
    object GetOrCreate(Type serviceType, string sessionId, Func<IServiceProvider, object> factory,
        IServiceProvider serviceProvider);

    /// <summary>
    /// HTTP Session 结束时调用，清理该 Session 的所有服务
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    void OnSessionEnd(string sessionId);
}
