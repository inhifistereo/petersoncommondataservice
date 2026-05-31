using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("calendar")]
public class CalendarController : ControllerBase
{
    private readonly CalendarService _calendarService;
    private readonly string _icsUrl;

    public CalendarController(CalendarService calendarService, IConfiguration configuration)
    {
        _calendarService = calendarService;
        _icsUrl = configuration["ICS-URL"] ?? throw new Exception("ICS-URL is not configured");
    }

    [HttpGet]
    public async Task<IActionResult> GetUpcomingEvents()
    {
        var events = await _calendarService.GetUpcomingEventsAsync(_icsUrl);
        return new JsonResult(events);
    }
}
