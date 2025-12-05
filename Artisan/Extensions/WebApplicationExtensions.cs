using System.Runtime.InteropServices;
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

    public static IEnumerable<KeyValuePair<string, string>> GetRuntimeInformation(this WebApplication app)
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        return
        [
            new KeyValuePair<string, string>("Application", app.Environment.ApplicationName),
            new KeyValuePair<string, string>("Environment", app.Environment.EnvironmentName),
            new KeyValuePair<string, string>("Content Root", app.Environment.ContentRootPath),
            new KeyValuePair<string, string>("Framework", RuntimeInformation.FrameworkDescription),
            new KeyValuePair<string, string>("OS Platform", RuntimeInformation.OSDescription),
            new KeyValuePair<string, string>("Process ID", process.Id.ToString()),
            new KeyValuePair<string, string>("Memory Usage", $"{process.WorkingSet64 / 1024 / 1024} MB"),
            new KeyValuePair<string, string>("Timezone", TimeZoneInfo.Local.DisplayName)
        ];
    }
}