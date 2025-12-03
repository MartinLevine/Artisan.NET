using System.Collections.Concurrent;

namespace Artisan.Session;

/// <summary>
/// Session 生命周期实现
/// 管理与 HTTP Session 绑定的服务实例
/// </summary>
public class SessionServiceFactory : ISessionServiceFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, SessionServiceBag> _sessionServices = new();

    private class SessionServiceBag
    {
        public ConcurrentDictionary<Type, object> Services { get; } = new();
        public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public T GetOrCreate<T>(string sessionId, Func<IServiceProvider, T> factory, IServiceProvider serviceProvider)
        where T : class
    {
        var bag = _sessionServices.GetOrAdd(sessionId, _ => new SessionServiceBag());
        bag.LastAccess = DateTime.UtcNow;
        return (T)bag.Services.GetOrAdd(typeof(T), _ => factory(serviceProvider));
    }

    /// <inheritdoc />
    public object GetOrCreate(Type serviceType, string sessionId, Func<IServiceProvider, object> factory,
        IServiceProvider serviceProvider)
    {
        var bag = _sessionServices.GetOrAdd(sessionId, _ => new SessionServiceBag());
        bag.LastAccess = DateTime.UtcNow;
        return bag.Services.GetOrAdd(serviceType, _ => factory(serviceProvider));
    }

    /// <inheritdoc />
    public void OnSessionEnd(string sessionId)
    {
        if (_sessionServices.TryRemove(sessionId, out var bag))
        {
            DisposeServices(bag.Services.Values);
        }
    }

    private void DisposeServices(IEnumerable<object> services)
    {
        foreach (var service in services)
        {
            if (service is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else if (service is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public void Dispose()
    {
        foreach (var bag in _sessionServices.Values)
        {
            DisposeServices(bag.Services.Values);
        }

        _sessionServices.Clear();
        GC.SuppressFinalize(this);
    }
}
