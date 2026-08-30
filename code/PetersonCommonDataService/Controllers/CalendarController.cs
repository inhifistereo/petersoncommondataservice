using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Models;
using PetersonCommonDataService.Services;

namespace PetersonCommonDataService.Controllers;

[ApiController]
[Route("calendar")]
public sealed class CalendarController(
    CalendarService calendarService,
    IOptions<CalendarOptions> options,
    TimeProvider timeProvider,
    ILogger<CalendarController> logger) : ControllerBase
{
    private const int MaxDays = 30;
    private const int MaxRangeDays = 90;

    private readonly CalendarOptions _options = options.Value;

    /// <summary>
    /// Upcoming events for the wall display.
    /// </summary>
    /// <param name="days">Window length in days from today. Ignored when from/to are supplied.</param>
    /// <param name="from">Inclusive start date (yyyy-MM-dd). Must be paired with <paramref name="to"/>.</param>
    /// <param name="to">Inclusive end date (yyyy-MM-dd). Must be paired with <paramref name="from"/>.</param>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CalendarEvent>>>> GetUpcomingEvents(
        [FromQuery] int? days,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if ((from is null) != (to is null))
        {
            return Problem(
                title: "Invalid request",
                detail: "'from' and 'to' must be supplied together.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var timeZone = calendarService.TimeZone;
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);

        DateOnly startDate;
        DateOnly endDateExclusive;

        if (from is not null && to is not null)
        {
            if (to < from)
            {
                return Problem(
                    title: "Invalid request",
                    detail: "'to' must not be earlier than 'from'.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (from.Value.AddDays(MaxRangeDays) < to.Value)
            {
                return Problem(
                    title: "Invalid request",
                    detail: $"The requested range exceeds the {MaxRangeDays}-day maximum.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            startDate = from.Value;
            endDateExclusive = to.Value.AddDays(1);
        }
        else
        {
            var requestedDays = Math.Clamp(days ?? _options.DefaultDays, 1, MaxDays);
            startDate = today;
            endDateExclusive = today.AddDays(requestedDays);
        }

        var windowStart = ToOffset(startDate, timeZone);
        var windowEnd = ToOffset(endDateExclusive, timeZone);

        logger.LogInformation("Serving calendar window {WindowStart} to {WindowEnd}", windowStart, windowEnd);

        var events = await calendarService.GetEventsAsync(windowStart, windowEnd, cancellationToken);

        return Ok(new ApiResponse<IReadOnlyList<CalendarEvent>>(
            events,
            new ResponseMeta
            {
                Source = "ics",
                FetchedAt = timeProvider.GetUtcNow(),
                TtlSeconds = 360,
            }));
    }

    /// <summary>Midnight on the given local date, carrying that date's real UTC offset.</summary>
    private static DateTimeOffset ToOffset(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }
}
