using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

[ApiController]
[Route("calendar")]
public class CalendarController : ControllerBase
{
    private readonly CalendarService _calendarService;
    private readonly string _icsUrl;

    public CalendarController(CalendarService calendarService)
    {
        _calendarService = calendarService;
        _icsUrl = Environment.GetEnvironmentVariable("ICS_URL") ?? throw new Exception("ICS_URL environment variable not set");
    }

    [HttpGet]
    public async Task<IActionResult> GetUpcomingEvents()
    {
        var events = await _calendarService.GetUpcomingEventsAsync(_icsUrl);
        return Ok(events);
    }
}
