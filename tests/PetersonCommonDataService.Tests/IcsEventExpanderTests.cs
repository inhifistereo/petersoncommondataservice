using Microsoft.Extensions.Logging.Abstractions;
using PetersonCommonDataService.Models;
using PetersonCommonDataService.Services;

namespace PetersonCommonDataService.Tests;

/// <summary>
/// Covers ICS semantics: recurrence, all-day boundaries, DST, and the malformed-but-legal
/// shapes that previously threw. The expander is a pure function, so none of this needs a
/// network or a clock.
/// </summary>
public sealed class IcsEventExpanderTests
{
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static readonly IcsEventExpander Expander = new(NullLogger<IcsEventExpander>.Instance);

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\n" +
        string.Join("\r\n", events) +
        "\r\nEND:VCALENDAR\r\n";

    private static IReadOnlyList<CalendarEvent> Expand(string ics, string from, string to) =>
        Expander.Expand(
            ics,
            DateTimeOffset.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
            Central);

    [Fact]
    public void TimedEvent_CarriesRealUtcOffset()
    {
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:timed-1
            SUMMARY:Movie night
            DTSTART;TZID=America/Chicago:20260830T193000
            DTEND;TZID=America/Chicago:20260830T200000
            END:VEVENT
            """);

        var result = Expand(ics, "2026-08-30T00:00:00-05:00", "2026-08-31T00:00:00-05:00");

        var e = Assert.Single(result);
        Assert.False(e.AllDay);
        // The offset is the whole point: without it a browser reads this as viewer-local.
        Assert.Equal("2026-08-30T19:30:00-05:00", e.Start);
        Assert.Equal("2026-08-30T20:00:00-05:00", e.End);
        Assert.Equal("2026-08-30", e.StartDate);
    }

    [Fact]
    public void AllDayEvent_ConvertsExclusiveDtEndToInclusiveEndDate()
    {
        // DTEND 0903 is exclusive in ICS, so the event actually covers 0901-0902.
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:allday-1
            SUMMARY:Trip
            DTSTART;VALUE=DATE:20260901
            DTEND;VALUE=DATE:20260903
            END:VEVENT
            """);

        var result = Expand(ics, "2026-09-01T00:00:00-05:00", "2026-09-05T00:00:00-05:00");

        var e = Assert.Single(result);
        Assert.True(e.AllDay);
        Assert.Equal("2026-09-01", e.Start);
        Assert.Equal("2026-09-02", e.End);
        Assert.Equal("2026-09-02", e.EndDate);
    }

    [Fact]
    public void SingleDayAllDayEvent_HasEqualStartAndEnd()
    {
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:allday-2
            SUMMARY:First day of school
            DTSTART;VALUE=DATE:20260901
            DTEND;VALUE=DATE:20260902
            END:VEVENT
            """);

        var e = Assert.Single(Expand(ics, "2026-09-01T00:00:00-05:00", "2026-09-03T00:00:00-05:00"));
        Assert.Equal("2026-09-01", e.Start);
        Assert.Equal("2026-09-01", e.End);
    }

    [Fact]
    public void RecurrenceIdOverride_ReplacesTheMasterOccurrence_RatherThanDuplicatingIt()
    {
        // This is the bug the Ical.Net upgrade fixed: expanding per-VEVENT emitted both
        // the master's original 11:30 slot and the override's moved 12:00 slot, so a
        // rescheduled appointment appeared twice on the wall.
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:series-1
            SUMMARY:Standup
            DTSTART;TZID=America/Chicago:20260903T113000
            DTEND;TZID=America/Chicago:20260903T120000
            RRULE:FREQ=WEEKLY;BYDAY=TH;COUNT=3
            END:VEVENT
            """,
            """
            BEGIN:VEVENT
            UID:series-1
            RECURRENCE-ID;TZID=America/Chicago:20260910T113000
            SUMMARY:Standup
            DTSTART;TZID=America/Chicago:20260910T120000
            DTEND;TZID=America/Chicago:20260910T123000
            END:VEVENT
            """);

        var result = Expand(ics, "2026-09-10T00:00:00-05:00", "2026-09-11T00:00:00-05:00");

        var e = Assert.Single(result);
        Assert.Equal("2026-09-10T12:00:00-05:00", e.Start);
    }

    [Fact]
    public void RecurringEvent_ExpandsWithinWindowOnly()
    {
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:weekly-1
            SUMMARY:Weekly
            DTSTART;TZID=America/Chicago:20260903T090000
            DTEND;TZID=America/Chicago:20260903T093000
            RRULE:FREQ=WEEKLY;BYDAY=TH;COUNT=10
            END:VEVENT
            """);

        var result = Expand(ics, "2026-09-03T00:00:00-05:00", "2026-09-18T00:00:00-05:00");

        // Three Thursdays fall inside the window; the fourth (09-24) is past the end.
        Assert.Equal(["2026-09-03", "2026-09-10", "2026-09-17"], result.Select(e => e.StartDate));
    }

    [Fact]
    public void ExdateOccurrence_IsExcluded()
    {
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:weekly-2
            SUMMARY:Weekly
            DTSTART;TZID=America/Chicago:20260903T090000
            DTEND;TZID=America/Chicago:20260903T093000
            RRULE:FREQ=WEEKLY;BYDAY=TH;COUNT=5
            EXDATE;TZID=America/Chicago:20260910T090000
            END:VEVENT
            """);

        var result = Expand(ics, "2026-09-03T00:00:00-05:00", "2026-09-18T00:00:00-05:00");

        Assert.Equal(["2026-09-03", "2026-09-17"], result.Select(e => e.StartDate));
    }

    [Fact]
    public void EventCrossingDstBoundary_ShiftsOffsetFromMinusFiveToMinusSix()
    {
        // US DST ends 2026-11-01. Same wall-clock time, different real offset either side.
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:dst-1
            SUMMARY:Weekly
            DTSTART;TZID=America/Chicago:20261029T090000
            DTEND;TZID=America/Chicago:20261029T093000
            RRULE:FREQ=WEEKLY;BYDAY=TH;COUNT=3
            END:VEVENT
            """);

        var result = Expand(ics, "2026-10-29T00:00:00-05:00", "2026-11-13T00:00:00-06:00");

        Assert.Equal("2026-10-29T09:00:00-05:00", result[0].Start);
        Assert.Equal("2026-11-05T09:00:00-06:00", result[1].Start);
    }

    [Fact]
    public void EventWithoutDtEnd_DoesNotThrowAndGetsAnEnd()
    {
        // Legal ICS: DTEND may be absent entirely. The old code dereferenced it and threw.
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:no-end-1
            SUMMARY:Instant
            DTSTART;TZID=America/Chicago:20260903T090000
            END:VEVENT
            """);

        var e = Assert.Single(Expand(ics, "2026-09-03T00:00:00-05:00", "2026-09-04T00:00:00-05:00"));
        Assert.Equal("2026-09-03T09:00:00-05:00", e.Start);
        Assert.False(string.IsNullOrEmpty(e.End));
    }

    [Fact]
    public void EventWithoutSummary_FallsBackToPlaceholder()
    {
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:no-summary-1
            DTSTART;TZID=America/Chicago:20260903T090000
            DTEND;TZID=America/Chicago:20260903T093000
            END:VEVENT
            """);

        var e = Assert.Single(Expand(ics, "2026-09-03T00:00:00-05:00", "2026-09-04T00:00:00-05:00"));
        Assert.Equal("(No title)", e.Subject);
    }

    [Fact]
    public void CancelledEvent_IsSurfacedRatherThanDropped()
    {
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:cancelled-1
            SUMMARY:Called off
            STATUS:CANCELLED
            DTSTART;TZID=America/Chicago:20260903T090000
            DTEND;TZID=America/Chicago:20260903T093000
            END:VEVENT
            """);

        var e = Assert.Single(Expand(ics, "2026-09-03T00:00:00-05:00", "2026-09-04T00:00:00-05:00"));
        Assert.True(e.IsCancelled);
    }

    [Fact]
    public void Events_AreSortedByDateThenAllDayFirstThenTime()
    {
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:sort-late
            SUMMARY:Afternoon
            DTSTART;TZID=America/Chicago:20260903T150000
            DTEND;TZID=America/Chicago:20260903T160000
            END:VEVENT
            """,
            """
            BEGIN:VEVENT
            UID:sort-early
            SUMMARY:Morning
            DTSTART;TZID=America/Chicago:20260903T080000
            DTEND;TZID=America/Chicago:20260903T090000
            END:VEVENT
            """,
            """
            BEGIN:VEVENT
            UID:sort-allday
            SUMMARY:Holiday
            DTSTART;VALUE=DATE:20260903
            DTEND;VALUE=DATE:20260904
            END:VEVENT
            """);

        var result = Expand(ics, "2026-09-03T00:00:00-05:00", "2026-09-04T00:00:00-05:00");

        Assert.Equal(["Holiday", "Morning", "Afternoon"], result.Select(e => e.Subject));
    }

    [Fact]
    public void OccurrenceIds_AreStablePerOccurrenceAndDistinctAcrossARecurrence()
    {
        var ics = Calendar(
            """
            BEGIN:VEVENT
            UID:ids-1
            SUMMARY:Weekly
            DTSTART;TZID=America/Chicago:20260903T090000
            DTEND;TZID=America/Chicago:20260903T093000
            RRULE:FREQ=WEEKLY;BYDAY=TH;COUNT=3
            END:VEVENT
            """);

        var first = Expand(ics, "2026-09-03T00:00:00-05:00", "2026-09-18T00:00:00-05:00");
        var second = Expand(ics, "2026-09-03T00:00:00-05:00", "2026-09-18T00:00:00-05:00");

        Assert.Equal(first.Select(e => e.Id), second.Select(e => e.Id));
        Assert.Equal(first.Count, first.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void MalformedIcs_Throws_RatherThanSilentlyReturningNoEvents()
    {
        // Deliberate: an unparseable feed must not look like "nothing on today". It has to
        // surface so CalendarService can translate it into an upstream failure and the
        // cache can fall back to last-known-good.
        Assert.ThrowsAny<Exception>(() => Expander.Expand(
            "this is not a calendar",
            DateTimeOffset.Parse("2026-09-03T00:00:00-05:00", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-09-04T00:00:00-05:00", System.Globalization.CultureInfo.InvariantCulture),
            Central));
    }
}
