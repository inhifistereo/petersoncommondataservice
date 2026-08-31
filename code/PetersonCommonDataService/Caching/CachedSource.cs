using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using PetersonCommonDataService.Errors;

namespace PetersonCommonDataService.Caching;

public interface ICachedSource
{
    Task<CachedResult<T>> GetAsync<T>(
        string key,
        TimeSpan ttl,
        TimeSpan lastGoodLifetime,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken);
}

/// <summary>
/// Caches upstream results and, crucially, keeps serving the last good value when the
/// upstream fails.
/// </summary>
/// <remarks>
/// Neither IMemoryCache nor HybridCache offers serve-on-error, which is the behaviour a
/// wall display actually needs — so this wrapper exists rather than a second cache
/// library. Two entries are held per key: a short-lived "fresh" entry that drives normal
/// hits, and a long-lived "last good" entry that survives an outage. A per-key semaphore
/// collapses concurrent misses into one upstream call.
/// </remarks>
public sealed class CachedSource(
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<CachedSource> logger) : ICachedSource
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private sealed record Entry<T>(T Value, DateTimeOffset FetchedAt);

    public async Task<CachedResult<T>> GetAsync<T>(
        string key,
        TimeSpan ttl,
        TimeSpan lastGoodLifetime,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        var freshKey = $"fresh:{key}";
        var lastGoodKey = $"lastgood:{key}";

        if (cache.TryGetValue(freshKey, out Entry<T>? cached) && cached is not null)
        {
            return Fresh(cached, ttl);
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have populated it while we waited.
            if (cache.TryGetValue(freshKey, out cached) && cached is not null)
            {
                return Fresh(cached, ttl);
            }

            try
            {
                var value = await factory(cancellationToken);
                var entry = new Entry<T>(value, timeProvider.GetUtcNow());

                cache.Set(freshKey, entry, ttl);
                cache.Set(lastGoodKey, entry, lastGoodLifetime);

                return Fresh(entry, ttl);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ClassifyFailure(ex);

                if (cache.TryGetValue(lastGoodKey, out Entry<T>? lastGood) && lastGood is not null)
                {
                    logger.LogWarning(
                        ex,
                        "Upstream {Key} failed ({Reason}); serving last-known-good data from {FetchedAt}",
                        key, reason, lastGood.FetchedAt);

                    return new CachedResult<T>
                    {
                        Value = lastGood.Value,
                        FetchedAt = lastGood.FetchedAt,
                        Stale = true,
                        StaleReason = reason,
                        TtlSeconds = (int)ttl.TotalSeconds,
                    };
                }

                // Nothing to fall back on — a cold start into a broken upstream.
                logger.LogError(ex, "Upstream {Key} failed ({Reason}) with no cached fallback", key, reason);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static CachedResult<T> Fresh<T>(Entry<T> entry, TimeSpan ttl) => new()
    {
        Value = entry.Value,
        FetchedAt = entry.FetchedAt,
        Stale = false,
        TtlSeconds = (int)ttl.TotalSeconds,
    };

    private static string ClassifyFailure(Exception exception) => exception switch
    {
        UpstreamException { UpstreamStatus: HttpStatusCode.TooManyRequests } => StaleReasons.UpstreamRateLimited,
        TaskCanceledException or TimeoutException => StaleReasons.UpstreamTimeout,
        UpstreamException { InnerException: TaskCanceledException or TimeoutException } => StaleReasons.UpstreamTimeout,
        _ => StaleReasons.UpstreamError,
    };
}
