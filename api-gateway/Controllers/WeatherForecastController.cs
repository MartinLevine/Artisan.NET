using api_gateway.Components;
using Artisan.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace api_gateway.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    // 属性注入测试
    [Inject]
    public IWeatherForecastService? PropertyInjectedService { get; set; }

    // 构造函数注入（保留用于对比）
    private readonly IWeatherForecastService _service;

    public WeatherForecastController(IWeatherForecastService service)
    {
        _service = service;
    }

    [HttpGet(Name = "GetWeatherForecast")]
    public IEnumerable<WeatherForecast> Get()
    {
        // 验证两种注入方式都有效
        var result1 = _service.RandomGet();
        var result2 = PropertyInjectedService?.RandomGet() ?? result1;

        // 如果两个都有效，返回任意一个；如果属性注入失败，PropertyInjectedService会为null
        return PropertyInjectedService != null ? result2 : result1;
    }

    [HttpGet("injection-status")]
    public IActionResult GetInjectionStatus()
    {
        return Ok(new
        {
            constructorInjection = _service != null ? "OK" : "FAILED",
            propertyInjection = PropertyInjectedService != null ? "OK" : "FAILED",
            isSameInstance = ReferenceEquals(_service, PropertyInjectedService)
        });
    }
}