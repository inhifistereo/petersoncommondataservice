using Microsoft.Extensions.Options;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Models;

namespace PetersonCommonDataService.Services.Weather;

/// <summary>
/// Caches the forecast and alerts separately, then composes them into one snapshot.
/// </summary>
/// <remarks>
/// Separate caches because the two have very different urgencies: a forecast is fine for
/// fifteen minutes, a tornado warning is not. Alerts also degrade to empty rather than
/// failing the request, since losing the forecast to an alert-endpoint blip would be a bad
/// trade.
/// </remarks>
public sealed class WeatherService(
    IWeatherProvider provider,
    ICachedSource cache,
    IOptions<WeatherOptions> options,
    ILogger<WeatherService> logger)
{
    private readonly WeatherOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<CachedResult<WeatherSnapshot>> GetWeatherAsync(CancellationToken cancellationToken)
    {
        var forecast = await cache.GetAsync(
            "weather:forecast",
            TimeSpan.FromSeconds(_options.CacheSeconds),
            TimeSpan.FromHours(_options.LastGoodHours),
            provider.GetForecastAsync,
            cancellationToken);

        var alerts = await GetAlertsAsync(cancellationToken);

        return forecast with { Value = forecast.Value with { Alerts = alerts } };
    }

    private async Task<IReadOnlyList<WeatherAlert>> GetAlertsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cached = await cache.GetAsync(
                "weather:alerts",
                TimeSpan.FromSeconds(_options.AlertCacheSeconds),
                TimeSpan.FromHours(_options.LastGoodHours),
                provider.GetAlertsAsync,
                cancellationToken);

            return cached.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read weather alerts; serving the forecast without them");
            return [];
        }
    }
}
