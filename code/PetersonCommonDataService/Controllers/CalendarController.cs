using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("calendar")]
public class CalendarController : ControllerBase
{
    private readonly CalendarService _calendarService;
    private readonly string _icsUrl;

    public CalendarController(CalendarService calendarService)
    {
        _calendarService = calendarService;
        _icsUrl = Environment.GetEnvironmentVariable("ICS-URL") ?? throw new Exception("ICS-URL environment variable not set");
    }

    [HttpGet]
    public async Task<IActionResult> GetUpcomingEvents()
    {
        var events = await _calendarService.GetUpcomingEventsAsync(_icsUrl);
        return new JsonResult(events);
    }
}
