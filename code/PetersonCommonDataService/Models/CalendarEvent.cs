namespace PetersonCommonDataService.Models;

/// <summary>
/// A single calendar occurrence as the wall display consumes it.
/// </summary>
/// <remarks>
/// Timed events carry a real UTC offset so a browser can parse them unambiguously.
/// The previous contract emitted "2026-08-30T19:30:00" with no offset, which JavaScript
/// interprets as the viewer's local time — correct only by coincidence, and wrong across
/// a DST boundary.
/// <para>
/// All-day events use date-only strings, and <see cref="EndDate"/> is <em>inclusive</em>
/// (the last day the event covers), unlike the exclusive DTEND in the ICS source.
/// </para>
/// </remarks>
public sealed record CalendarEvent
{
    /// <summary>Stable id for this occurrence, derived from the event UID and its start.</summary>
    public required string Id { get; init; }

    public required string Subject { get; init; }

    public required bool AllDay { get; init; }

    /// <summary>ISO-8601 with offset for timed events; "yyyy-MM-dd" for all-day.</summary>
    public required string Start { get; init; }

    /// <summary>ISO-8601 with offset for timed events; "yyyy-MM-dd" (inclusive) for all-day.</summary>
    public required string End { get; init; }

    /// <summary>Local calendar date the occurrence starts on. Lets the display group by day with a string compare.</summary>
    public required string StartDate { get; init; }

    /// <summary>Local calendar date the occurrence ends on, inclusive.</summary>
    public required string EndDate { get; init; }

    public string? Location { get; init; }

    /// <summary>True when the organiser cancelled it; surfaced so the display can dim rather than hide it.</summary>
    public bool IsCancelled { get; init; }
}
