using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.Modules;

public abstract class ArtisanModule
{
    // 钩子：是否应该加载此模块 (用于条件加载)
    public virtual bool ShouldLoad(IConfiguration configuration) => true;

    // 钩子：注册服务
    public virtual void ConfigureServices(IServiceCollection services)
    {
    }

    // 钩子：配置中间件
    public virtual void Configure(WebApplication app)
    {
    }
}