namespace PetersonCommonDataService.Models;

/// <summary>Everything the display needs to render weather, in one payload.</summary>
public sealed record WeatherSnapshot
{
    public required WeatherLocation Location { get; init; }
    public required WeatherUnits Units { get; init; }

    /// <summary>
    /// Null when the observation station is unreachable or reporting nulls. The forecast is
    /// still useful without it, so a missing current reading degrades rather than fails.
    /// </summary>
    public CurrentConditions? Current { get; init; }

    public required IReadOnlyList<DailyForecast> Daily { get; init; }
    public required IReadOnlyList<HourlyForecast> Hourly { get; init; }
    public required IReadOnlyList<WeatherAlert> Alerts { get; init; }
}

public sealed record WeatherLocation
{
    public string? City { get; init; }
    public string? State { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
}

/// <summary>
/// Units are normalised server-side and stated explicitly. The display should never do a
/// conversion of its own.
/// </summary>
public sealed record WeatherUnits
{
    public string Temperature { get; init; } = "F";
    public string WindSpeed { get; init; } = "mph";
}

public sealed record CurrentConditions
{
    public required DateTimeOffset ObservedAt { get; init; }
    public int? Temperature { get; init; }

    /// <summary>Heat index or wind chill when NWS reports one, otherwise the plain temperature.</summary>
    public int? ApparentTemperature { get; init; }

    public int? Humidity { get; init; }
    public int? WindSpeed { get; init; }
    public int? WindDirection { get; init; }
    public required string Condition { get; init; }
    public string? ConditionText { get; init; }
    public bool IsDay { get; init; }
}

public sealed record DailyForecast
{
    /// <summary>Local calendar date, yyyy-MM-dd.</summary>
    public required string Date { get; init; }

    public int? High { get; init; }
    public int? Low { get; init; }
    public required string Condition { get; init; }
    public string? ConditionText { get; init; }
    public int? PrecipitationProbability { get; init; }

    /// <summary>NWS prose, e.g. "Sunny, with a high near 97". Useful for a detail panel.</summary>
    public string? DetailedForecast { get; init; }
}

public sealed record HourlyForecast
{
    /// <summary>ISO-8601 with offset.</summary>
    public required string Time { get; init; }

    public int? Temperature { get; init; }
    public int? PrecipitationProbability { get; init; }
    public required string Condition { get; init; }
}

/// <summary>An active NWS advisory, watch or warning.</summary>
public sealed record WeatherAlert
{
    public required string Id { get; init; }

    /// <summary>e.g. "Severe Thunderstorm Warning".</summary>
    public required string Event { get; init; }

    /// <summary>Extreme, Severe, Moderate, Minor or Unknown.</summary>
    public string? Severity { get; init; }

    public string? Urgency { get; init; }
    public string? Headline { get; init; }
    public DateTimeOffset? Effective { get; init; }
    public DateTimeOffset? Expires { get; init; }
}
