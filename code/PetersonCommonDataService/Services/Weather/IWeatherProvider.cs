using PetersonCommonDataService.Models;

namespace PetersonCommonDataService.Services.Weather;

/// <summary>
/// A source of forecast data. Implemented by <see cref="NwsWeatherProvider"/>; the
/// abstraction exists so a keyed provider can replace it without touching the controller,
/// the cache, or the display.
/// </summary>
public interface IWeatherProvider
{
    /// <summary>Current conditions plus daily and hourly forecast. Excludes alerts.</summary>
    Task<WeatherSnapshot> GetForecastAsync(CancellationToken cancellationToken);

    /// <summary>Active advisories, watches and warnings for the configured point.</summary>
    Task<IReadOnlyList<WeatherAlert>> GetAlertsAsync(CancellationToken cancellationToken);
}
