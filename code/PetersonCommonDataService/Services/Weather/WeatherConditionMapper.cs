namespace PetersonCommonDataService.Services.Weather;

/// <summary>
/// Translates a provider's condition encoding into this service's own vocabulary.
/// </summary>
/// <remarks>
/// This is the seam that matters. NWS expresses conditions as icon URLs, other providers
/// use WMO codes or numeric ids; normalising here means the display's icon set survives a
/// provider swap without a single change on the client.
/// </remarks>
public static class WeatherConditionMapper
{
    /// <summary>The full vocabulary. Anything unrecognised becomes <c>unknown</c>.</summary>
    public static class Conditions
    {
        public const string Clear = "clear";
        public const string MostlyClear = "mostly-clear";
        public const string PartlyCloudy = "partly-cloudy";
        public const string MostlyCloudy = "mostly-cloudy";
        public const string Cloudy = "cloudy";
        public const string Fog = "fog";
        public const string Drizzle = "drizzle";
        public const string Rain = "rain";
        public const string HeavyRain = "heavy-rain";
        public const string FreezingRain = "freezing-rain";
        public const string Sleet = "sleet";
        public const string Snow = "snow";
        public const string HeavySnow = "heavy-snow";
        public const string Thunderstorm = "thunderstorm";
        public const string Windy = "windy";
        public const string Hot = "hot";
        public const string Cold = "cold";
        public const string Haze = "haze";
        public const string Smoke = "smoke";
        public const string Severe = "severe";
        public const string Unknown = "unknown";
    }

    private static readonly Dictionary<string, string> NwsTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["skc"] = Conditions.Clear,
        ["few"] = Conditions.MostlyClear,
        ["sct"] = Conditions.PartlyCloudy,
        ["bkn"] = Conditions.MostlyCloudy,
        ["ovc"] = Conditions.Cloudy,
        ["wind_skc"] = Conditions.Windy,
        ["wind_few"] = Conditions.Windy,
        ["wind_sct"] = Conditions.Windy,
        ["wind_bkn"] = Conditions.Windy,
        ["wind_ovc"] = Conditions.Windy,
        ["fog"] = Conditions.Fog,
        ["haze"] = Conditions.Haze,
        ["smoke"] = Conditions.Smoke,
        ["dust"] = Conditions.Haze,
        ["hot"] = Conditions.Hot,
        ["cold"] = Conditions.Cold,
        ["blizzard"] = Conditions.HeavySnow,
        ["snow"] = Conditions.Snow,
        ["sleet"] = Conditions.Sleet,
        ["fzra"] = Conditions.FreezingRain,
        ["rain_fzra"] = Conditions.FreezingRain,
        ["snow_fzra"] = Conditions.FreezingRain,
        ["rain_snow"] = Conditions.Snow,
        ["rain_sleet"] = Conditions.Sleet,
        ["snow_sleet"] = Conditions.Sleet,
        ["rain"] = Conditions.Rain,
        ["rain_showers"] = Conditions.Rain,
        ["rain_showers_hi"] = Conditions.Rain,
        ["tsra"] = Conditions.Thunderstorm,
        ["tsra_sct"] = Conditions.Thunderstorm,
        ["tsra_hi"] = Conditions.Thunderstorm,
        ["tornado"] = Conditions.Severe,
        ["hurricane"] = Conditions.Severe,
        ["tropical_storm"] = Conditions.Severe,
    };

    /// <summary>
    /// Maps an NWS icon URL such as
    /// <c>https://api.weather.gov/icons/land/night/tsra,60?size=medium</c> to a condition.
    /// </summary>
    /// <remarks>
    /// The URL encodes one or two conditions after the day/night segment, each optionally
    /// suffixed with a precipitation probability (<c>rain,60</c>). The first is the dominant
    /// one, so that is what the display gets.
    /// </remarks>
    public static string FromNwsIcon(string? iconUrl)
    {
        if (string.IsNullOrWhiteSpace(iconUrl))
        {
            return Conditions.Unknown;
        }

        var path = iconUrl.Split('?', 2)[0];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Walk forward from the day/night marker; everything after it is a condition.
        var start = Array.FindIndex(segments, s =>
            s.Equals("day", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("night", StringComparison.OrdinalIgnoreCase));

        if (start < 0 || start + 1 >= segments.Length)
        {
            return Conditions.Unknown;
        }

        var token = segments[start + 1].Split(',', 2)[0];
        return NwsTokens.TryGetValue(token, out var condition) ? condition : Conditions.Unknown;
    }

    /// <summary>True when the icon URL denotes a daytime icon.</summary>
    public static bool IsDaytimeIcon(string? iconUrl) =>
        iconUrl?.Contains("/day/", StringComparison.OrdinalIgnoreCase) ?? true;
}
