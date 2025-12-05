using Microsoft.AspNetCore.Mvc;

namespace Emm.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet("constructor-injection")]
    public async Task<object> ConstructorInjection()
    {
        return new
        {
            message = "注入成功"
        };
    }
}