using System.Text.Json.Serialization;

namespace PetersonCommonDataService.Services.Weather;

// Wire shapes for api.weather.gov. Internal to the provider: nothing here reaches the
// public API, which is the point of mapping onto WeatherSnapshot.

internal sealed class NwsEnvelope<T>
{
    [JsonPropertyName("properties")]
    public T? Properties { get; set; }
}

internal sealed class NwsPointProperties
{
    [JsonPropertyName("gridId")]
    public string GridId { get; set; } = string.Empty;

    [JsonPropertyName("gridX")]
    public int GridX { get; set; }

    [JsonPropertyName("gridY")]
    public int GridY { get; set; }

    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }

    [JsonPropertyName("relativeLocation")]
    public NwsRelativeLocation? RelativeLocation { get; set; }
}

internal sealed class NwsRelativeLocation
{
    [JsonPropertyName("properties")]
    public NwsRelativeLocationProperties? Properties { get; set; }
}

internal sealed class NwsRelativeLocationProperties
{
    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }
}

internal sealed class NwsForecastProperties
{
    [JsonPropertyName("periods")]
    public List<NwsPeriod> Periods { get; set; } = [];
}

internal sealed class NwsPeriod
{
    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    [JsonPropertyName("isDaytime")]
    public bool IsDaytime { get; set; }

    [JsonPropertyName("temperature")]
    public int? Temperature { get; set; }

    [JsonPropertyName("temperatureUnit")]
    public string? TemperatureUnit { get; set; }

    [JsonPropertyName("probabilityOfPrecipitation")]
    public NwsQuantity? ProbabilityOfPrecipitation { get; set; }

    [JsonPropertyName("shortForecast")]
    public string? ShortForecast { get; set; }

    [JsonPropertyName("detailedForecast")]
    public string? DetailedForecast { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

/// <summary>A measured value plus its unit. NWS reports null values freely.</summary>
internal sealed class NwsQuantity
{
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("unitCode")]
    public string? UnitCode { get; set; }
}

internal sealed class NwsStationCollection
{
    [JsonPropertyName("features")]
    public List<NwsStationFeature> Features { get; set; } = [];
}

internal sealed class NwsStationFeature
{
    [JsonPropertyName("properties")]
    public NwsStationProperties? Properties { get; set; }
}

internal sealed class NwsStationProperties
{
    [JsonPropertyName("stationIdentifier")]
    public string StationIdentifier { get; set; } = string.Empty;
}

internal sealed class NwsObservationProperties
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("temperature")]
    public NwsQuantity? Temperature { get; set; }

    [JsonPropertyName("heatIndex")]
    public NwsQuantity? HeatIndex { get; set; }

    [JsonPropertyName("windChill")]
    public NwsQuantity? WindChill { get; set; }

    [JsonPropertyName("relativeHumidity")]
    public NwsQuantity? RelativeHumidity { get; set; }

    [JsonPropertyName("windSpeed")]
    public NwsQuantity? WindSpeed { get; set; }

    [JsonPropertyName("windDirection")]
    public NwsQuantity? WindDirection { get; set; }

    [JsonPropertyName("textDescription")]
    public string? TextDescription { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

internal sealed class NwsAlertCollection
{
    [JsonPropertyName("features")]
    public List<NwsAlertFeature> Features { get; set; } = [];
}

internal sealed class NwsAlertFeature
{
    [JsonPropertyName("properties")]
    public NwsAlertProperties? Properties { get; set; }
}

internal sealed class NwsAlertProperties
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("urgency")]
    public string? Urgency { get; set; }

    [JsonPropertyName("headline")]
    public string? Headline { get; set; }

    [JsonPropertyName("effective")]
    public DateTimeOffset? Effective { get; set; }

    [JsonPropertyName("expires")]
    public DateTimeOffset? Expires { get; set; }
}
