using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.Extensions;

/// <summary>
/// 拓展WebApplication类
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// 获取所有已注册的 API 路由信息
    /// </summary>
    /// <param name="app">App实例</param>
    public static IEnumerable<ControllerActionDescriptor> GetEndpoints(this WebApplication app)
    {
        var actionProvider = app.Services.GetService<IActionDescriptorCollectionProvider>();

        // 如果没注册 MVC，这个服务可能为空
        if (actionProvider == null)
        {
            return [];
        }

        return actionProvider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>() // 只关心 Controller，忽略 Razor Pages
            .OrderBy(x => x.ControllerName)
            .ThenBy(x => x.AttributeRouteInfo?.Template ?? "N/A")
            .ToList();
    }
}