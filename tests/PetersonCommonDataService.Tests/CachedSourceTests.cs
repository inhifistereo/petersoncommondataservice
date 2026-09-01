using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Errors;

namespace PetersonCommonDataService.Tests;

/// <summary>
/// The display's "never blank" guarantee lives here, so these assertions matter more than
/// most: a fresh hit must not call upstream, a failure must fall back to last-known-good,
/// and only a failure with nothing cached at all may surface an error.
/// </summary>
public sealed class CachedSourceTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LastGood = TimeSpan.FromHours(12);

    private static (CachedSource Source, FakeTimeProvider Clock) Build()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-31T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var cache = new MemoryCache(new MemoryCacheOptions { Clock = new FakeSystemClock(clock) });
        return (new CachedSource(cache, clock, NullLogger<CachedSource>.Instance), clock);
    }

    /// <summary>Bridges FakeTimeProvider into MemoryCache so entry expiry advances with the test clock.</summary>
    private sealed class FakeSystemClock(TimeProvider timeProvider) : ISystemClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }

    [Fact]
    public async Task FreshHit_DoesNotCallUpstreamAgain()
    {
        var (source, _) = Build();
        var calls = 0;

        for (var i = 0; i < 5; i++)
        {
            await source.GetAsync("k", Ttl, LastGood, _ => { calls++; return Task.FromResult("value"); }, default);
        }

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FirstFetch_IsMarkedFreshWithTheFetchTime()
    {
        var (source, clock) = Build();

        var result = await source.GetAsync("k", Ttl, LastGood, _ => Task.FromResult("value"), default);

        Assert.False(result.Stale);
        Assert.Null(result.StaleReason);
        Assert.Equal(clock.GetUtcNow(), result.FetchedAt);
        Assert.Equal(60, result.TtlSeconds);
    }

    [Fact]
    public async Task UpstreamFailureAfterTtl_ServesLastKnownGoodAsStale()
    {
        var (source, clock) = Build();

        await source.GetAsync("k", Ttl, LastGood, _ => Task.FromResult("good"), default);
        var fetchedAt = clock.GetUtcNow();

        clock.Advance(TimeSpan.FromSeconds(90));

        var result = await source.GetAsync<string>(
            "k", Ttl, LastGood,
            _ => throw new UpstreamException("todoist", System.Net.HttpStatusCode.ServiceUnavailable, "down"),
            default);

        Assert.Equal("good", result.Value);
        Assert.True(result.Stale);
        Assert.Equal(StaleReasons.UpstreamError, result.StaleReason);
        // fetchedAt must describe the data, not the failed attempt.
        Assert.Equal(fetchedAt, result.FetchedAt);
    }

    [Fact]
    public async Task RateLimited_IsReportedDistinctlyFromAGenericError()
    {
        var (source, clock) = Build();
        await source.GetAsync("k", Ttl, LastGood, _ => Task.FromResult("good"), default);
        clock.Advance(TimeSpan.FromSeconds(90));

        var result = await source.GetAsync<string>(
            "k", Ttl, LastGood,
            _ => throw new UpstreamException("todoist", System.Net.HttpStatusCode.TooManyRequests, "slow down"),
            default);

        Assert.Equal(StaleReasons.UpstreamRateLimited, result.StaleReason);
    }

    [Fact]
    public async Task Timeout_IsReportedAsTimeout()
    {
        var (source, clock) = Build();
        await source.GetAsync("k", Ttl, LastGood, _ => Task.FromResult("good"), default);
        clock.Advance(TimeSpan.FromSeconds(90));

        var result = await source.GetAsync<string>(
            "k", Ttl, LastGood, _ => throw new TimeoutException(), default);

        Assert.Equal(StaleReasons.UpstreamTimeout, result.StaleReason);
    }

    [Fact]
    public async Task UpstreamFailureWithNothingCached_Throws()
    {
        var (source, _) = Build();

        // A cold start into a broken upstream has nothing honest to serve.
        await Assert.ThrowsAsync<UpstreamException>(() => source.GetAsync<string>(
            "k", Ttl, LastGood,
            _ => throw new UpstreamException("todoist", System.Net.HttpStatusCode.ServiceUnavailable, "down"),
            default));
    }

    [Fact]
    public async Task RecoveryAfterOutage_ClearsStaleAndAdvancesFetchedAt()
    {
        var (source, clock) = Build();
        await source.GetAsync("k", Ttl, LastGood, _ => Task.FromResult("old"), default);

        clock.Advance(TimeSpan.FromSeconds(90));
        await source.GetAsync<string>("k", Ttl, LastGood, _ => throw new TimeoutException(), default);

        clock.Advance(TimeSpan.FromSeconds(10));
        var recovered = await source.GetAsync("k", Ttl, LastGood, _ => Task.FromResult("new"), default);

        Assert.Equal("new", recovered.Value);
        Assert.False(recovered.Stale);
        Assert.Equal(clock.GetUtcNow(), recovered.FetchedAt);
    }

    [Fact]
    public async Task ConcurrentMisses_CollapseIntoASingleUpstreamCall()
    {
        var (source, _) = Build();
        var calls = 0;
        var release = new TaskCompletionSource();

        async Task<string> Slow(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return "value";
        }

        var inFlight = Enumerable.Range(0, 10)
            .Select(_ => source.GetAsync("k", Ttl, LastGood, Slow, default))
            .ToList();

        release.SetResult();
        await Task.WhenAll(inFlight);

        Assert.Equal(1, calls);
        Assert.All(inFlight, t => Assert.Equal("value", t.Result.Value));
    }

    [Fact]
    public async Task SeparateKeys_AreCachedIndependently()
    {
        var (source, _) = Build();

        var a = await source.GetAsync("a", Ttl, LastGood, _ => Task.FromResult("A"), default);
        var b = await source.GetAsync("b", Ttl, LastGood, _ => Task.FromResult("B"), default);

        Assert.Equal("A", a.Value);
        Assert.Equal("B", b.Value);
    }
}
