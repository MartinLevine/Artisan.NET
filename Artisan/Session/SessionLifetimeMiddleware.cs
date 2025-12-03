using Microsoft.AspNetCore.Http;

namespace Artisan.Session;

/// <summary>
/// Session 生命周期中间件
/// 监听 HTTP Session 销毁事件
/// </summary>
public class SessionLifetimeMiddleware : IMiddleware
{
    private readonly ISessionServiceFactory _sessionFactory;

    public SessionLifetimeMiddleware(ISessionServiceFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // 确保 Session 已启动
        if (context.Session.IsAvailable)
        {
            await context.Session.LoadAsync();

            // 注册 Session 结束回调
            context.Response.OnCompleted(() =>
            {
                // 检查 Session 是否被标记为清除或过期
                if (SessionIsEnding(context))
                {
                    _sessionFactory.OnSessionEnd(context.Session.Id);
                }

                return Task.CompletedTask;
            });
        }

        await next(context);
    }

    private bool SessionIsEnding(HttpContext context)
    {
        // 检查是否调用了 Session.Clear() 或 SignOut
        return context.Items.ContainsKey("__SessionCleared")
               || (context.Response.Headers.TryGetValue("Set-Cookie", out var cookies)
                   && cookies.ToString().Contains("expires=Thu, 01 Jan 1970"));
    }
}
