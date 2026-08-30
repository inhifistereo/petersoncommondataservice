namespace PetersonCommonDataService.Models;

/// <summary>
/// Standard wrapper for every successful response.
/// </summary>
/// <remarks>
/// Deliberately contains no per-request values (no "generatedAt", no "ageSeconds").
/// The body must be a pure function of cache state so an unchanged payload produces an
/// unchanged ETag and the display's poll can be answered with a 304. Request-relative
/// freshness travels in the HTTP <c>Age</c> header, which is still sent on a 304.
/// </remarks>
public sealed record ApiResponse<T>(T Data, ResponseMeta Meta);

/// <summary>Provenance and freshness of the accompanying <see cref="ApiResponse{T}.Data"/>.</summary>
public sealed record ResponseMeta
{
    /// <summary>Which upstream produced this data, e.g. "todoist" or "ics".</summary>
    public required string Source { get; init; }

    /// <summary>When the upstream was actually queried — a property of the cached data, not of this request.</summary>
    public required DateTimeOffset FetchedAt { get; init; }

    /// <summary>True when the upstream failed and this is last-known-good data.</summary>
    public bool Stale { get; init; }

    /// <summary>Why the data is stale: upstream_error, upstream_timeout, upstream_rate_limited.</summary>
    public string? StaleReason { get; init; }

    /// <summary>How long this data is considered fresh, for the client's own scheduling.</summary>
    public int TtlSeconds { get; init; }
}
