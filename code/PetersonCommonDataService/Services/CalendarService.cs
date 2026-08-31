using Microsoft.Extensions.Options;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Errors;
using PetersonCommonDataService.Models;

namespace PetersonCommonDataService.Services;

/// <summary>
/// Fetches the published ICS feed and expands it into display occurrences.
/// </summary>
/// <remarks>
/// Fetching is kept separate from parsing: the transport lives here, while all the
/// awkward calendar semantics live in the pure <see cref="IcsEventExpander"/>.
/// </remarks>
public sealed class CalendarService(
    HttpClient httpClient,
    IcsEventExpander expander,
    ICachedSource cache,
    TimeProvider timeProvider,
    IOptions<CalendarOptions> options,
    ILogger<CalendarService> logger)
{
    private const string UpstreamName = "ics";

    private readonly CalendarOptions _options = options.Value;

    /// <summary>Resolved once so an invalid configured zone fails loudly rather than silently defaulting.</summary>
    public TimeZoneInfo TimeZone { get; } = ResolveTimeZone(options.Value.TimeZone);

    /// <summary>
    /// Every occurrence in a deliberately wide window around today.
    /// </summary>
    /// <remarks>
    /// One wide expansion is cached and each request slices the portion it asked for, so
    /// varying <c>?days</c> cannot fan the cache out into an entry per value.
    /// </remarks>
    public Task<CachedResult<IReadOnlyList<CalendarEvent>>> GetAllEventsAsync(CancellationToken cancellationToken = default) =>
        cache.GetAsync(
            "calendar",
            TimeSpan.FromSeconds(_options.CacheSeconds),
            TimeSpan.FromHours(_options.LastGoodHours),
            ExpandWideWindowAsync,
            cancellationToken);

    private async Task<IReadOnlyList<CalendarEvent>> ExpandWideWindowAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZone).DateTime);
        var windowStart = ToOffset(today.AddDays(-_options.WindowLookbackDays), TimeZone);
        var windowEnd = ToOffset(today.AddDays(_options.WindowLookaheadDays), TimeZone);

        var ics = await DownloadAsync(cancellationToken);

        try
        {
            var events = expander.Expand(ics, windowStart, windowEnd, TimeZone);
            logger.LogInformation(
                "Expanded {EventCount} occurrences between {WindowStart} and {WindowEnd}",
                events.Count, windowStart, windowEnd);
            return events;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UpstreamException(UpstreamName, null, "The calendar feed could not be parsed.", ex);
        }
    }

    /// <summary>Midnight on the given local date, carrying that date's real UTC offset.</summary>
    public static DateTimeOffset ToOffset(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private async Task<string> DownloadAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(_options.IcsUrl, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new UpstreamException(UpstreamName, null, "Could not reach the calendar feed.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("ICS feed request failed with {StatusCode}", (int)response.StatusCode);
                throw new UpstreamException(
                    UpstreamName,
                    response.StatusCode,
                    $"The calendar feed returned {(int)response.StatusCode}.");
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        // .NET 8+ accepts IANA ids on every platform, so TimeZoneConverter is no longer
        // needed; the Windows-style ids in older config still resolve too.
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"Calendar:TimeZone '{timeZoneId}' is not a recognised time zone id.", ex);
        }
    }
}
