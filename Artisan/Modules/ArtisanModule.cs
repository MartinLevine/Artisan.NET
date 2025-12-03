using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.Modules;

/// <summary>
/// 模块基类
/// </summary>
public abstract class ArtisanModule
{
    /// <summary>
    /// 配置服务（对应 builder.Services.AddXxx）
    /// 通过构造函数注入配置类（使用 [AppSetting] 标记的类）
    /// </summary>
    public virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>
    /// 配置中间件（对应 app.UseXxx）
    /// </summary>
    public virtual void Configure(WebApplication app) { }
}
