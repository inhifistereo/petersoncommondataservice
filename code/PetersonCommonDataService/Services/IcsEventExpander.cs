using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ical.Net.DataTypes;
using PetersonCommonDataService.Models;
using VEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace PetersonCommonDataService.Services;

/// <summary>
/// Turns raw ICS text into display-ready occurrences.
/// </summary>
/// <remarks>
/// Deliberately a pure function of its arguments — no HttpClient, no clock, no
/// configuration — so recurrence, DST and all-day edge cases can be tested against
/// fixture files without a network.
/// </remarks>
public sealed class IcsEventExpander(ILogger<IcsEventExpander> logger)
{
    private const string TimedFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private const string DateFormat = "yyyy-MM-dd";

    public IReadOnlyList<CalendarEvent> Expand(
        string icsContent,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeZoneInfo timeZone)
    {
        var calendar = Ical.Net.Calendar.Load(icsContent);
        if (calendar is null)
        {
            logger.LogWarning("ICS content did not parse into a calendar; returning no events");
            return [];
        }

        var events = new List<CalendarEvent>();

        // GetOccurrences is lazy and ordered by start, and takes only a lower bound,
        // so the upper bound is applied with TakeWhile.
        var occurrences = calendar
            .GetOccurrences<VEvent>(new CalDateTime(windowStart.UtcDateTime, "UTC"))
            // A null start cannot be positioned in the ordered sequence, so it is passed
            // through to Map, which skips and logs it rather than truncating the window.
            .TakeWhile(occurrence =>
                occurrence.Period.StartTime is null ||
                occurrence.Period.StartTime.AsUtc < windowEnd.UtcDateTime);

        foreach (var occurrence in occurrences)
        {
            if (occurrence.Source is not VEvent source)
            {
                continue;
            }

            var mapped = Map(occurrence.Period, source, timeZone);
            if (mapped is not null)
            {
                events.Add(mapped);
            }
        }

        return [.. events
            .OrderBy(e => e.StartDate, StringComparer.Ordinal)
            .ThenBy(e => e.AllDay ? 0 : 1)
            .ThenBy(e => e.Start, StringComparer.Ordinal)];
    }

    private CalendarEvent? Map(Period period, VEvent source, TimeZoneInfo timeZone)
    {
        if (period.StartTime is null)
        {
            logger.LogWarning("Skipping event {Uid} with no start time", source.Uid);
            return null;
        }

        var subject = string.IsNullOrWhiteSpace(source.Summary) ? "(No title)" : source.Summary;
        var isCancelled = string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);

        if (source.IsAllDay)
        {
            var startDate = DateOnly.FromDateTime(period.StartTime.Value);

            // DTEND in ICS is exclusive; the display wants the last covered day.
            // EffectiveEndTime applies the RFC 5545 default-duration rules when DTEND
            // is absent, which a raw DtEnd access would NullReferenceException on.
            var endExclusive = period.EffectiveEndTime is { } effectiveEnd
                ? DateOnly.FromDateTime(effectiveEnd.Value)
                : startDate.AddDays(1);

            var endInclusive = endExclusive > startDate ? endExclusive.AddDays(-1) : startDate;

            return Build(
                source, subject, isCancelled, allDay: true,
                start: startDate.ToString(DateFormat, CultureInfo.InvariantCulture),
                end: endInclusive.ToString(DateFormat, CultureInfo.InvariantCulture),
                startDate: startDate, endDate: endInclusive);
        }

        var startLocal = ToLocalOffset(period.StartTime.AsUtc, timeZone);
        var endLocal = period.EffectiveEndTime is { } timedEnd
            ? ToLocalOffset(timedEnd.AsUtc, timeZone)
            : startLocal;

        return Build(
            source, subject, isCancelled, allDay: false,
            start: startLocal.ToString(TimedFormat, CultureInfo.InvariantCulture),
            end: endLocal.ToString(TimedFormat, CultureInfo.InvariantCulture),
            startDate: DateOnly.FromDateTime(startLocal.DateTime),
            endDate: DateOnly.FromDateTime(endLocal.DateTime));
    }

    private static CalendarEvent Build(
        VEvent source, string subject, bool isCancelled, bool allDay,
        string start, string end, DateOnly startDate, DateOnly endDate) =>
        new()
        {
            Id = StableId(source.Uid, start),
            Subject = subject,
            AllDay = allDay,
            Start = start,
            End = end,
            StartDate = startDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            EndDate = endDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            Location = string.IsNullOrWhiteSpace(source.Location) ? null : source.Location,
            IsCancelled = isCancelled,
        };

    /// <summary>Converts a UTC instant into the display time zone, carrying the real offset.</summary>
    private static DateTimeOffset ToLocalOffset(DateTime utc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    /// <summary>
    /// Stable per-occurrence id so the display can key and diff rather than re-render.
    /// Recurring events share a UID, so the occurrence start is part of the hash.
    /// </summary>
    private static string StableId(string? uid, string startToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{uid}|{startToken}"));
        return Convert.ToHexStringLower(bytes.AsSpan(0, 8));
    }
}
