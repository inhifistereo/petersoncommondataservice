using Microsoft.AspNetCore.Mvc;

namespace PetersonCommonDataService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ErrorController : ControllerBase
    {
        [HttpGet]
        public IActionResult Index(string message)
        {
            return Problem(detail: message, title: "Authentication Error");
        }
    }
}