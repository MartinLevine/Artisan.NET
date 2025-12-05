using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan;

/// <summary>
/// 可配置应用接口
/// 用户的 Application 类可选实现此接口，参与框架配置流程
/// 框架会将实现此接口的 Application 类隐式当作 Module 处理
/// </summary>
public interface IConfigurableApplication
{
    // ==========================================
    // 阶段 1：框架预配置 (最先执行)
    // ==========================================
    /// <summary>
    /// 配置 Artisan 框架选项
    /// 职责：禁用/替换自动加载的模块、配置框架底层行为
    /// 时机：WebApplicationBuilder 创建之前
    /// </summary>
    void ConfigureArtisan(ArtisanOptions options) { }

    // ==========================================
    // 阶段 2：服务注册 (中间执行)
    // ==========================================
    /// <summary>
    /// 配置服务（对应 builder.Services.AddXxx）
    /// 职责：用户注册自己的服务
    /// 时机：所有模块的 ConfigureServices 执行完毕后
    /// </summary>
    void ConfigureServices(IServiceCollection services) { }

    // ==========================================
    // 阶段 3：中间件配置 (最后执行)
    // ==========================================
    /// <summary>
    /// 配置中间件（对应 app.UseXxx）
    /// 职责：用户添加自己的中间件
    /// 时机：所有模块的 Configure 执行完毕后
    /// </summary>
    void Configure(WebApplication app) { }
}
