using Artisan.Modules;

namespace api_gateway.Modules;

[Module]
public class InitModule: ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddControllers();
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
        app.MapControllers();
    }
}