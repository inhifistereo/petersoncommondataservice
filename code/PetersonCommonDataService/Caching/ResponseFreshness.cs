using System.Globalization;
using PetersonCommonDataService.Models;

namespace PetersonCommonDataService.Caching;

public static class ResponseFreshness
{
    /// <summary>
    /// Emits Cache-Control and Age from the payload's own provenance, so a polling client
    /// knows how long the data stays valid and how old it already is.
    /// </summary>
    /// <remarks>
    /// Age is a header rather than a body field on purpose: it changes every second, and
    /// putting it in the body would change the ETag on every request and defeat 304s.
    /// Headers are still sent on a 304, so freshness survives a not-modified response.
    /// </remarks>
    public static void ApplyFreshness(this HttpResponse response, ResponseMeta meta, TimeProvider timeProvider)
    {
        var ageSeconds = Math.Max(0, (int)(timeProvider.GetUtcNow() - meta.FetchedAt).TotalSeconds);
        var remaining = Math.Max(0, meta.TtlSeconds - ageSeconds);

        response.Headers.CacheControl = $"private, max-age={remaining}";
        response.Headers.Age = ageSeconds.ToString(CultureInfo.InvariantCulture);
    }
}
