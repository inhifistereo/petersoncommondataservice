using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetersonCommonDataService.Caching;
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
        DateOnly endDateInclusive;

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
            endDateInclusive = to.Value;
        }
        else
        {
            var requestedDays = Math.Clamp(days ?? _options.DefaultDays, 1, MaxDays);
            startDate = today;
            endDateInclusive = today.AddDays(requestedDays - 1);
        }

        var cached = await calendarService.GetAllEventsAsync(cancellationToken);

        // The cached expansion covers a wide window; take only the requested slice.
        // Comparing the date strings is safe because both are yyyy-MM-dd, and it keeps
        // the filter in the display's own local-date terms.
        var startKey = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endKey = endDateInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var events = cached.Value
            .Where(e => string.CompareOrdinal(e.EndDate, startKey) >= 0
                     && string.CompareOrdinal(e.StartDate, endKey) <= 0)
            .ToList();

        logger.LogInformation(
            "Serving {EventCount} of {CachedCount} cached events for {StartKey}..{EndKey} (stale={Stale})",
            events.Count, cached.Value.Count, startKey, endKey, cached.Stale);

        var meta = new ResponseMeta
        {
            Source = "ics",
            FetchedAt = cached.FetchedAt,
            Stale = cached.Stale,
            StaleReason = cached.StaleReason,
            TtlSeconds = cached.TtlSeconds,
        };

        Response.ApplyFreshness(meta, timeProvider);

        return Ok(new ApiResponse<IReadOnlyList<CalendarEvent>>(events, meta));
    }
}
