using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.AspNetCore;

/// <summary>
/// Artisan Controller 激活器
/// 在 Controller 创建后自动调用属性注入
/// </summary>
public class ArtisanControllerActivator : IControllerActivator
{
    public object Create(ControllerContext context)
    {
        var controllerType = context.ActionDescriptor.ControllerTypeInfo.AsType();

        // 使用 ActivatorUtilities 创建 Controller（支持构造函数注入）
        var controller = ActivatorUtilities.CreateInstance(
            context.HttpContext.RequestServices,
            controllerType);

        // 获取属性注入器并注入
        var injector = context.HttpContext.RequestServices
            .GetService<DependencyInjection.IPropertyInjector>();
        injector?.InjectProperties(controller);

        return controller;
    }

    public void Release(ControllerContext context, object controller)
    {
        if (controller is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public ValueTask ReleaseAsync(ControllerContext context, object controller)
    {
        if (controller is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        Release(context, controller);
        return ValueTask.CompletedTask;
    }
}
