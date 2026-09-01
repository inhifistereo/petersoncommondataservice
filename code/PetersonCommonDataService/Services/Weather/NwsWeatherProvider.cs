using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Errors;
using PetersonCommonDataService.Models;

namespace PetersonCommonDataService.Services.Weather;

/// <summary>
/// Forecast data from the US National Weather Service (api.weather.gov).
/// </summary>
/// <remarks>
/// Free and keyless, but multi-step: a coordinate resolves to a forecast grid, and the grid
/// yields the forecast URLs. The grid never moves, so it is cached for a long time and the
/// per-refresh cost is just the forecast calls.
/// <para>
/// Two shapes to be careful with. Forecasts arrive in Fahrenheit already, while station
/// observations are metric and need converting. And the daily forecast is a list of
/// day/night <em>periods</em>, not calendar days, so periods are paired into days here.
/// </para>
/// </remarks>
public sealed class NwsWeatherProvider(
    HttpClient httpClient,
    ICachedSource cache,
    IOptions<WeatherOptions> options,
    ILogger<NwsWeatherProvider> logger) : IWeatherProvider
{
    private const string UpstreamName = "nws";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly WeatherOptions _options = options.Value;

    private string Point => FormattableString.Invariant($"{_options.Latitude},{_options.Longitude}");

    public async Task<WeatherSnapshot> GetForecastAsync(CancellationToken cancellationToken)
    {
        var grid = await GetGridAsync(cancellationToken);

        var forecastTask = GetAsync<NwsEnvelope<NwsForecastProperties>>(
            $"gridpoints/{grid.GridId}/{grid.GridX},{grid.GridY}/forecast", cancellationToken);
        var hourlyTask = GetAsync<NwsEnvelope<NwsForecastProperties>>(
            $"gridpoints/{grid.GridId}/{grid.GridX},{grid.GridY}/forecast/hourly", cancellationToken);

        await Task.WhenAll(forecastTask, hourlyTask);

        var daily = BuildDaily(forecastTask.Result?.Properties?.Periods ?? []);
        var hourly = BuildHourly(hourlyTask.Result?.Properties?.Periods ?? []);

        return new WeatherSnapshot
        {
            Location = new WeatherLocation
            {
                City = grid.RelativeLocation?.Properties?.City,
                State = grid.RelativeLocation?.Properties?.State,
                Latitude = _options.Latitude!.Value,
                Longitude = _options.Longitude!.Value,
            },
            Units = new WeatherUnits(),
            Current = await GetCurrentAsync(grid, cancellationToken),
            Daily = daily,
            Hourly = hourly,
            Alerts = [],
        };
    }

    public async Task<IReadOnlyList<WeatherAlert>> GetAlertsAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync<NwsAlertCollection>(
            $"alerts/active?point={Uri.EscapeDataString(Point)}", cancellationToken);

        return response?.Features
            .Select(f => f.Properties)
            .Where(p => p is not null && !string.IsNullOrEmpty(p.Id))
            .Select(p => new WeatherAlert
            {
                Id = p!.Id!,
                Event = p.Event ?? "Weather alert",
                Severity = p.Severity,
                Urgency = p.Urgency,
                Headline = p.Headline,
                Effective = p.Effective,
                Expires = p.Expires,
            })
            .ToList() ?? [];
    }

    /// <summary>
    /// Resolves the coordinate to an NWS forecast grid. Cached for a long time: the mapping
    /// is static, and re-resolving it on every refresh would double the upstream calls.
    /// </summary>
    private async Task<NwsPointProperties> GetGridAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(
            $"weather:grid:{Point}",
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(90),
            async ct =>
            {
                var point = await GetAsync<NwsEnvelope<NwsPointProperties>>(
                    $"points/{Uri.EscapeDataString(Point)}", ct);

                return point?.Properties
                    ?? throw new UpstreamException(UpstreamName, null, "NWS returned no grid for the configured point.");
            },
            cancellationToken);

        return cached.Value;
    }

    private async Task<CurrentConditions?> GetCurrentAsync(NwsPointProperties grid, CancellationToken cancellationToken)
    {
        try
        {
            var stations = await cache.GetAsync(
                $"weather:station:{grid.GridId}/{grid.GridX},{grid.GridY}",
                TimeSpan.FromDays(30),
                TimeSpan.FromDays(90),
                async ct =>
                {
                    var collection = await GetAsync<NwsStationCollection>(
                        $"gridpoints/{grid.GridId}/{grid.GridX},{grid.GridY}/stations", ct);
                    return collection?.Features.FirstOrDefault()?.Properties?.StationIdentifier;
                },
                cancellationToken);

            if (string.IsNullOrEmpty(stations.Value))
            {
                return null;
            }

            var observation = await GetAsync<NwsEnvelope<NwsObservationProperties>>(
                $"stations/{stations.Value}/observations/latest", cancellationToken);

            var p = observation?.Properties;
            if (p is null)
            {
                return null;
            }

            var temperature = CelsiusToFahrenheit(p.Temperature?.Value);
            var apparent = CelsiusToFahrenheit(p.HeatIndex?.Value ?? p.WindChill?.Value) ?? temperature;

            return new CurrentConditions
            {
                ObservedAt = p.Timestamp,
                Temperature = temperature,
                ApparentTemperature = apparent,
                Humidity = Round(p.RelativeHumidity?.Value),
                WindSpeed = KilometresToMiles(p.WindSpeed?.Value),
                WindDirection = Round(p.WindDirection?.Value),
                Condition = WeatherConditionMapper.FromNwsIcon(p.Icon),
                ConditionText = p.TextDescription,
                IsDay = WeatherConditionMapper.IsDaytimeIcon(p.Icon),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing current reading should not cost the display its forecast.
            logger.LogWarning(ex, "Could not read current conditions; returning forecast without them");
            return null;
        }
    }

    /// <summary>
    /// Collapses NWS day/night periods into calendar days.
    /// </summary>
    /// <remarks>
    /// NWS returns "Today"/"Tonight"/"Wednesday"/... rather than days. Passing that list
    /// straight through would give the display two entries per date with no high/low, so
    /// daytime periods supply the high and night-time periods the low.
    /// </remarks>
    private List<DailyForecast> BuildDaily(List<NwsPeriod> periods)
    {
        var days = new List<DailyForecast>();
        var byDate = periods.GroupBy(p => p.StartTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        foreach (var group in byDate)
        {
            var day = group.FirstOrDefault(p => p.IsDaytime);
            var night = group.FirstOrDefault(p => !p.IsDaytime);
            var representative = day ?? night;
            if (representative is null)
            {
                continue;
            }

            days.Add(new DailyForecast
            {
                Date = group.Key,
                High = day?.Temperature,
                Low = night?.Temperature,
                Condition = WeatherConditionMapper.FromNwsIcon(representative.Icon),
                ConditionText = representative.ShortForecast,
                PrecipitationProbability = Round(representative.ProbabilityOfPrecipitation?.Value),
                DetailedForecast = representative.DetailedForecast,
            });
        }

        return [.. days.Take(_options.ForecastDays)];
    }

    private List<HourlyForecast> BuildHourly(List<NwsPeriod> periods) =>
    [
        .. periods.Take(_options.ForecastHours).Select(p => new HourlyForecast
        {
            Time = p.StartTime.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            Temperature = p.Temperature,
            PrecipitationProbability = Round(p.ProbabilityOfPrecipitation?.Value),
            Condition = WeatherConditionMapper.FromNwsIcon(p.Icon),
        }),
    ];

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new UpstreamException(UpstreamName, null, "Could not reach the National Weather Service.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "NWS request to {Url} failed with {StatusCode}: {Body}", url, (int)response.StatusCode, body);

                throw new UpstreamException(
                    UpstreamName, response.StatusCode, $"NWS returned {(int)response.StatusCode} for '{url}'.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new UpstreamException(UpstreamName, response.StatusCode, "NWS returned an unparseable response.", ex);
            }
        }
    }

    private static int? Round(double? value) => value is null ? null : (int)Math.Round(value.Value);

    private static int? CelsiusToFahrenheit(double? celsius) =>
        celsius is null ? null : (int)Math.Round((celsius.Value * 9 / 5) + 32);

    private static int? KilometresToMiles(double? kmh) =>
        kmh is null ? null : (int)Math.Round(kmh.Value * 0.621371);
}
