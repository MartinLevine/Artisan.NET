using Artisan.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ProCode.Hosting.Controllers;

public interface IWeatherForecastService
{
    String Get();
}

[Service(key: "wechat")]
public class WeatherForecastService : IWeatherForecastService
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    public String Get()
    {
        return Summaries[Random.Shared.Next(Summaries.Length)];
    }
}

[Service(key: "alipay")]
public class WTFService : IWeatherForecastService
{
    public String Get()
    {
        return "这是Wee";
    }
}

[ApiController]
[Route("api/[controller]")]
public class WeatherForecastController : ControllerBase
{
    private readonly IWeatherForecastService _service;

    public WeatherForecastController(
        [FromKeyedServices("wechat")] IWeatherForecastService service)
    {
        _service = service;
    }

    [HttpGet(Name = "GetWeatherForecast")]
    public String Get()
    {
        return _service.Get();
    }
}