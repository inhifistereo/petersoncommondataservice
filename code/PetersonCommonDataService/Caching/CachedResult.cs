namespace PetersonCommonDataService.Caching;

/// <summary>Cached data plus the provenance the display needs to judge it.</summary>
public sealed record CachedResult<T>
{
    public required T Value { get; init; }

    /// <summary>When the upstream was actually queried — not when this request arrived.</summary>
    public required DateTimeOffset FetchedAt { get; init; }

    /// <summary>True when the upstream failed and this is last-known-good data.</summary>
    public bool Stale { get; init; }

    /// <summary>Short token describing why: upstream_error, upstream_timeout, upstream_rate_limited.</summary>
    public string? StaleReason { get; init; }

    public required int TtlSeconds { get; init; }
}

/// <summary>Reasons a payload is being served stale. Kept as constants so the display can switch on them.</summary>
public static class StaleReasons
{
    public const string UpstreamError = "upstream_error";
    public const string UpstreamTimeout = "upstream_timeout";
    public const string UpstreamRateLimited = "upstream_rate_limited";
}
