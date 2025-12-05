using Artisan.Attributes;
using Artisan.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Emm.API;

[Module]
public class EmmModule : ArtisanModule
{
    public override bool ShouldLoad(IConfiguration configuration) => false;

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
    }

    public override void Configure(WebApplication app)
    {
        app.MapControllers();
    }
}