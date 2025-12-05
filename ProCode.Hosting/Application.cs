using Artisan;
using Artisan.Attributes;
using Artisan.Modules;

namespace ProCode.Hosting;

[Module]
public class AppModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddOpenApi();
    }

    public override void Configure(WebApplication app)
    {
        base.Configure(app);
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        app.UseHttpsRedirection();
        app.UseAuthorization();
    }
}

[ArtisanApplication]
public class Application : IConfigurableApplication
{
    public static void Main(string[] args)
    {
        ArtisanApplicationV2.Run<Application>(args);
    }
}