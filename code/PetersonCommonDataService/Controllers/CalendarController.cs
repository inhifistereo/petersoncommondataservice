using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetersonCommonDataService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Threading.Tasks;

namespace PetersonCommonDataService.Controllers
{
    [Authorize] // Ensures only authenticated users can access
    [ApiController]
    [Route("calendar")]
    public class CalendarController : ControllerBase
    {
        private readonly CalendarService _calendarService;

        public CalendarController(CalendarService calendarService)
        {
            _calendarService = calendarService;
        }

        [HttpGet]
        public async Task<IActionResult> GetCalendarEvents()
        {
            var events = await _calendarService.GetCalendarEventsAsync();
            return Ok(events);
        }

        [HttpGet("login")]
        public IActionResult Login()
        {
            return Challenge(new AuthenticationProperties { RedirectUri = "/" }, OpenIdConnectDefaults.AuthenticationScheme);
        }
    }
}
