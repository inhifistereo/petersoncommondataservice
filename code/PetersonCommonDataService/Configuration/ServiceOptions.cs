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
}

/// <summary>Browser origins permitted to call the API.</summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}
