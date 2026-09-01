using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Services.Weather;

namespace PetersonCommonDataService.Tests;

/// <summary>
/// Exercises the NWS provider against canned payloads. The two things most likely to be
/// wrong are the day/night period pairing and the unit conversion, since forecasts arrive
/// in Fahrenheit while station observations are metric.
/// </summary>
public sealed class NwsWeatherProviderTests
{
    /// <summary>Serves canned JSON per URL fragment and records what was requested.</summary>
    private sealed class StubHandler(Dictionary<string, string> routes) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            var match = routes.FirstOrDefault(r => url.Contains(r.Key, StringComparison.Ordinal));
            if (match.Value is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(match.Value, Encoding.UTF8, "application/json"),
            });
        }
    }

    private const string PointsJson = """
    { "properties": { "gridId": "EAX", "gridX": 43, "gridY": 42,
      "relativeLocation": { "properties": { "city": "Kansas City", "state": "MO" } } } }
    """;

    private const string ForecastJson = """
    { "properties": { "periods": [
        { "startTime": "2026-09-01T06:00:00-05:00", "isDaytime": true,  "temperature": 97,
          "temperatureUnit": "F", "shortForecast": "Sunny",
          "probabilityOfPrecipitation": { "value": 10 },
          "detailedForecast": "Sunny, with a high near 97",
          "icon": "https://api.weather.gov/icons/land/day/hot?size=medium" },
        { "startTime": "2026-09-01T18:00:00-05:00", "isDaytime": false, "temperature": 77,
          "temperatureUnit": "F", "shortForecast": "Clear",
          "probabilityOfPrecipitation": { "value": 0 },
          "icon": "https://api.weather.gov/icons/land/night/skc?size=medium" },
        { "startTime": "2026-09-02T06:00:00-05:00", "isDaytime": true,  "temperature": 96,
          "temperatureUnit": "F", "shortForecast": "Thunderstorms",
          "probabilityOfPrecipitation": { "value": 60 },
          "icon": "https://api.weather.gov/icons/land/day/tsra,60?size=medium" }
    ] } }
    """;

    private const string HourlyJson = """
    { "properties": { "periods": [
        { "startTime": "2026-09-01T20:00:00-05:00", "isDaytime": false, "temperature": 91,
          "probabilityOfPrecipitation": { "value": 0 },
          "icon": "https://api.weather.gov/icons/land/night/skc?size=medium" },
        { "startTime": "2026-09-01T21:00:00-05:00", "isDaytime": false, "temperature": 87,
          "probabilityOfPrecipitation": { "value": 5 },
          "icon": "https://api.weather.gov/icons/land/night/few?size=medium" }
    ] } }
    """;

    private const string StationsJson = """
    { "features": [ { "properties": { "stationIdentifier": "KOJC" } } ] }
    """;

    // Metric, as NWS reports observations: 32C and 5.544 km/h.
    private const string ObservationJson = """
    { "properties": { "timestamp": "2026-09-01T00:35:00+00:00",
      "temperature": { "value": 32, "unitCode": "wmoUnit:degC" },
      "heatIndex": { "value": 33.49, "unitCode": "wmoUnit:degC" },
      "windChill": { "value": null },
      "relativeHumidity": { "value": 46.17 },
      "windSpeed": { "value": 5.544, "unitCode": "wmoUnit:km_h-1" },
      "windDirection": { "value": 170 },
      "textDescription": "Clear",
      "icon": "https://api.weather.gov/icons/land/night/skc?size=medium" } }
    """;

    private const string AlertsJson = """
    { "features": [ { "properties": {
        "id": "urn:oid:2.49.0.1.840.0.abc", "event": "Heat Advisory",
        "severity": "Moderate", "urgency": "Expected",
        "headline": "Heat Advisory until 8 PM",
        "effective": "2026-09-01T10:00:00-05:00",
        "expires": "2026-09-01T20:00:00-05:00" } } ] }
    """;

    private static Dictionary<string, string> DefaultRoutes() => new()
    {
        // Order matters: matching is first-substring-wins, so more specific routes come
        // first. "/observations/latest" must precede "/stations" because the observation
        // URL is stations/{id}/observations/latest, and "/forecast" must come last or it
        // would swallow "/forecast/hourly".
        ["/points/"] = PointsJson,
        ["/observations/latest"] = ObservationJson,
        ["/forecast/hourly"] = HourlyJson,
        ["/stations"] = StationsJson,
        ["/alerts/active"] = AlertsJson,
        ["/forecast"] = ForecastJson,
    };

    private static (NwsWeatherProvider Provider, StubHandler Handler) Build(Dictionary<string, string>? routes = null)
    {
        var handler = new StubHandler(routes ?? DefaultRoutes());
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.weather.gov/") };
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var cache = new CachedSource(new MemoryCache(new MemoryCacheOptions()), clock, NullLogger<CachedSource>.Instance);
        var options = Options.Create(new WeatherOptions { Latitude = 38.9, Longitude = -94.6 });

        return (new NwsWeatherProvider(client, cache, options, NullLogger<NwsWeatherProvider>.Instance), handler);
    }

    [Fact]
    public async Task DayAndNightPeriods_ArePairedIntoOneDayWithHighAndLow()
    {
        // NWS returns "Today"/"Tonight" periods, not calendar days. Passing them through
        // unpaired would give the display two entries per date and no high/low.
        var (provider, _) = Build();

        var snapshot = await provider.GetForecastAsync(default);

        var first = snapshot.Daily[0];
        Assert.Equal("2026-09-01", first.Date);
        Assert.Equal(97, first.High);
        Assert.Equal(77, first.Low);
    }

    [Fact]
    public async Task DailyConditionComesFromTheDaytimePeriod()
    {
        var (provider, _) = Build();

        var snapshot = await provider.GetForecastAsync(default);

        Assert.Equal("hot", snapshot.Daily[0].Condition);
        Assert.Equal("thunderstorm", snapshot.Daily[1].Condition);
        Assert.Equal(60, snapshot.Daily[1].PrecipitationProbability);
    }

    [Fact]
    public async Task CurrentConditions_AreConvertedFromMetricToTheStatedUnits()
    {
        var (provider, _) = Build();

        var snapshot = await provider.GetForecastAsync(default);

        Assert.NotNull(snapshot.Current);
        Assert.Equal(90, snapshot.Current!.Temperature);       // 32C
        Assert.Equal(92, snapshot.Current.ApparentTemperature); // 33.49C heat index
        Assert.Equal(3, snapshot.Current.WindSpeed);            // 5.544 km/h
        Assert.Equal("F", snapshot.Units.Temperature);
        Assert.Equal("mph", snapshot.Units.WindSpeed);
    }

    [Fact]
    public async Task CurrentConditions_ReportNightFromTheIcon()
    {
        var (provider, _) = Build();

        var snapshot = await provider.GetForecastAsync(default);

        Assert.False(snapshot.Current!.IsDay);
        Assert.Equal("clear", snapshot.Current.Condition);
    }

    [Fact]
    public async Task HourlyForecast_CarriesOffsetBearingTimestamps()
    {
        var (provider, _) = Build();

        var snapshot = await provider.GetForecastAsync(default);

        Assert.Equal("2026-09-01T20:00:00-05:00", snapshot.Hourly[0].Time);
        Assert.Equal(91, snapshot.Hourly[0].Temperature);
    }

    [Fact]
    public async Task LocationIsResolvedFromThePointLookup()
    {
        var (provider, _) = Build();

        var snapshot = await provider.GetForecastAsync(default);

        Assert.Equal("Kansas City", snapshot.Location.City);
        Assert.Equal("MO", snapshot.Location.State);
    }

    [Fact]
    public async Task GridLookupIsCached_SoARefreshDoesNotReResolveIt()
    {
        var (provider, handler) = Build();

        await provider.GetForecastAsync(default);
        await provider.GetForecastAsync(default);

        // The coordinate-to-grid mapping is static; re-resolving it would double the calls.
        Assert.Equal(1, handler.Requested.Count(u => u.Contains("/points/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UnavailableObservationStation_StillYieldsAForecast()
    {
        // Losing the current reading must not cost the display its forecast.
        var routes = DefaultRoutes();
        routes.Remove("/observations/latest");
        routes.Remove("/stations");
        var (provider, _) = Build(routes);

        var snapshot = await provider.GetForecastAsync(default);

        Assert.Null(snapshot.Current);
        Assert.NotEmpty(snapshot.Daily);
    }

    [Fact]
    public async Task ActiveAlertsAreMapped()
    {
        var (provider, _) = Build();

        var alerts = await provider.GetAlertsAsync(default);

        var alert = Assert.Single(alerts);
        Assert.Equal("Heat Advisory", alert.Event);
        Assert.Equal("Moderate", alert.Severity);
        Assert.Equal("Heat Advisory until 8 PM", alert.Headline);
    }

    [Fact]
    public async Task NoActiveAlerts_YieldsAnEmptyListRatherThanNull()
    {
        var routes = DefaultRoutes();
        routes["/alerts/active"] = """{ "features": [] }""";
        var (provider, _) = Build(routes);

        Assert.Empty(await provider.GetAlertsAsync(default));
    }
}
