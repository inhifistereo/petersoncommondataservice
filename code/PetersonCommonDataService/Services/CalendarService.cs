using Microsoft.Extensions.Options;
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
    IOptions<CalendarOptions> options,
    ILogger<CalendarService> logger)
{
    private const string UpstreamName = "ics";

    private readonly CalendarOptions _options = options.Value;

    /// <summary>Resolved once so an invalid configured zone fails loudly rather than silently defaulting.</summary>
    public TimeZoneInfo TimeZone { get; } = ResolveTimeZone(options.Value.TimeZone);

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken = default)
    {
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
