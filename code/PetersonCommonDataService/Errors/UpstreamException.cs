using System.Net;

namespace PetersonCommonDataService.Errors;

/// <summary>
/// Thrown when a third-party dependency (Todoist, the ICS feed, a weather provider)
/// fails. Carries enough context for the handler to emit a 502 naming the culprit,
/// so an upstream outage is diagnosable from the response alone.
/// </summary>
/// <remarks>
/// The upstream response <em>body</em> deliberately does not travel on this exception.
/// It can contain tokens or personal data and must only ever be logged, never returned.
/// </remarks>
public sealed class UpstreamException : Exception
{
    public UpstreamException(string upstreamName, HttpStatusCode? upstreamStatus, string message, Exception? inner = null)
        : base(message, inner)
    {
        UpstreamName = upstreamName;
        UpstreamStatus = upstreamStatus;
    }

    /// <summary>Which dependency failed, e.g. "todoist" or "ics".</summary>
    public string UpstreamName { get; }

    /// <summary>Status the upstream returned, when it returned one at all.</summary>
    public HttpStatusCode? UpstreamStatus { get; }
}
