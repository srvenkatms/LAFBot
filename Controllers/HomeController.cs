using Microsoft.AspNetCore.Mvc;
[ApiController]
public class HomeController : ControllerBase
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        return Ok("Welcome to my application!");
    }
}