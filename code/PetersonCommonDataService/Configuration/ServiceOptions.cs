using System.ComponentModel.DataAnnotations;

namespace PetersonCommonDataService.Configuration;

/// <summary>
/// Calendar (ICS) settings. <see cref="IcsUrl"/> is supplied by the deployment as the
/// flat env var <c>ICS-URL</c>; the rest come from the <c>Calendar</c> config section.
/// </summary>
public sealed class CalendarOptions
{
    public const string SectionName = "Calendar";

    [Required(AllowEmptyStrings = false, ErrorMessage = "ICS-URL is not configured")]
    public string IcsUrl { get; set; } = string.Empty;

    /// <summary>IANA or Windows time zone id used to localise event times.</summary>
    public string TimeZone { get; set; } = "America/Chicago";

    /// <summary>Default number of days returned by GET /calendar when ?days is absent.</summary>
    [Range(1, 30)]
    public int DefaultDays { get; set; } = 5;

    /// <summary>
    /// How long an expansion stays fresh. The published ICS is itself hours stale and the
    /// download is the slowest upstream call, so a tighter window buys nothing.
    /// </summary>
    [Range(30, 3600)]
    public int CacheSeconds { get; set; } = 360;

    /// <summary>How long a successful expansion is retained to serve during an outage.</summary>
    [Range(1, 168)]
    public int LastGoodHours { get; set; } = 24;

    /// <summary>Days before today included in the cached wide window.</summary>
    [Range(0, 7)]
    public int WindowLookbackDays { get; set; } = 1;

    /// <summary>Days after today included in the cached wide window. Requests slice from this.</summary>
    [Range(7, 400)]
    public int WindowLookaheadDays { get; set; } = 35;
}

/// <summary>
/// Todoist settings. Both values are supplied by the deployment as the flat env vars
/// <c>TODOIST-API-KEY</c> and <c>TODOIST-PROJECT-ID</c>.
/// </summary>
public sealed class TodoistOptions
{
    public const string SectionName = "Todoist";

    [Required(AllowEmptyStrings = false, ErrorMessage = "TODOIST-API-KEY is not configured")]
    public string ApiKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "TODOIST-PROJECT-ID is not configured")]
    public string ProjectId { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.todoist.com/api/v1/";

    /// <summary>Label a task must carry to appear on the wall display.</summary>
    public string DisplayLabel { get; set; } = "DakBoard";

    /// <summary>
    /// How long a task list stays fresh. Deliberately below the display's refresh interval:
    /// a checked-off chore should leave the wall promptly, and the call volume is trivial.
    /// </summary>
    [Range(15, 3600)]
    public int CacheSeconds { get; set; } = 90;

    /// <summary>How long a successful fetch is retained to serve during an outage.</summary>
    [Range(1, 168)]
    public int LastGoodHours { get; set; } = 12;
}

/// <summary>Browser origins permitted to call the API.</summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>API access control.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// Accepted API keys, comma-separated. More than one is allowed so a key can be
    /// rotated without a window where the display is locked out: add the new key, move
    /// the display over, then drop the old one.
    /// </summary>
    /// <remarks>
    /// A single delimited string rather than an array because this arrives as one
    /// Container App secret injected into one environment variable.
    /// </remarks>
    public string Keys { get; set; } = string.Empty;

    public IReadOnlyList<string> ParsedKeys =>
        Keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool HasKeys => ParsedKeys.Count > 0;
}

/// <summary>
/// Weather settings. Coordinates arrive from the deployment as secrets rather than living
/// in the repo, so a home location is not committed to source control.
/// </summary>
public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }

    /// <summary>
    /// NWS requires a User-Agent identifying the caller and rejects requests without one.
    /// </summary>
    public string UserAgent { get; set; } = "PetersonCommonDataService (github.com/inhifistereo/petersoncommondataservice)";

    /// <summary>Days of daily forecast to return.</summary>
    [Range(1, 7)]
    public int ForecastDays { get; set; } = 5;

    /// <summary>Hours of hourly forecast to return.</summary>
    [Range(1, 156)]
    public int ForecastHours { get; set; } = 12;

    /// <summary>Forecast freshness. NWS updates roughly hourly.</summary>
    [Range(60, 3600)]
    public int CacheSeconds { get; set; } = 900;

    /// <summary>
    /// Alerts are cached far more briefly than the forecast. A severe-weather warning is
    /// the highest-value thing on the display and must not sit behind a 15 minute window.
    /// </summary>
    [Range(30, 900)]
    public int AlertCacheSeconds { get; set; } = 300;

    /// <summary>
    /// How long a forecast is retained to serve during an outage. Deliberately short:
    /// weather older than this is worse than showing nothing.
    /// </summary>
    [Range(1, 24)]
    public int LastGoodHours { get; set; } = 6;

    public bool IsConfigured => Latitude is not null && Longitude is not null;
}
