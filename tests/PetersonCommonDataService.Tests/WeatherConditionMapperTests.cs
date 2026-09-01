using PetersonCommonDataService.Services.Weather;

namespace PetersonCommonDataService.Tests;

/// <summary>
/// The mapper is the seam that keeps the display's icon set stable across a provider
/// change, so its vocabulary is worth pinning down.
/// </summary>
public sealed class WeatherConditionMapperTests
{
    [Theory]
    [InlineData("https://api.weather.gov/icons/land/day/skc?size=medium", "clear")]
    [InlineData("https://api.weather.gov/icons/land/night/skc?size=medium", "clear")]
    [InlineData("https://api.weather.gov/icons/land/day/few?size=medium", "mostly-clear")]
    [InlineData("https://api.weather.gov/icons/land/day/sct?size=medium", "partly-cloudy")]
    [InlineData("https://api.weather.gov/icons/land/day/bkn?size=medium", "mostly-cloudy")]
    [InlineData("https://api.weather.gov/icons/land/day/ovc?size=medium", "cloudy")]
    [InlineData("https://api.weather.gov/icons/land/day/rain?size=medium", "rain")]
    [InlineData("https://api.weather.gov/icons/land/day/tsra?size=medium", "thunderstorm")]
    [InlineData("https://api.weather.gov/icons/land/day/snow?size=medium", "snow")]
    [InlineData("https://api.weather.gov/icons/land/day/fzra?size=medium", "freezing-rain")]
    [InlineData("https://api.weather.gov/icons/land/day/fog?size=medium", "fog")]
    [InlineData("https://api.weather.gov/icons/land/day/hot?size=medium", "hot")]
    [InlineData("https://api.weather.gov/icons/land/day/wind_bkn?size=medium", "windy")]
    [InlineData("https://api.weather.gov/icons/land/day/tornado?size=medium", "severe")]
    public void KnownIcons_MapToTheServiceVocabulary(string icon, string expected) =>
        Assert.Equal(expected, WeatherConditionMapper.FromNwsIcon(icon));

    [Fact]
    public void IconWithPrecipitationProbability_IgnoresTheSuffix()
    {
        // NWS appends a probability to the token, e.g. "tsra,60".
        Assert.Equal("thunderstorm", WeatherConditionMapper.FromNwsIcon(
            "https://api.weather.gov/icons/land/night/tsra,60?size=medium"));
    }

    [Fact]
    public void IconWithTwoConditions_UsesTheDominantFirstOne()
    {
        Assert.Equal("rain", WeatherConditionMapper.FromNwsIcon(
            "https://api.weather.gov/icons/land/day/rain,40/tsra,20?size=medium"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://api.weather.gov/icons/land/day/not_a_real_token?size=medium")]
    [InlineData("totally-unexpected")]
    public void UnrecognisedInput_IsUnknownRatherThanThrowing(string? icon) =>
        Assert.Equal("unknown", WeatherConditionMapper.FromNwsIcon(icon));

    [Theory]
    [InlineData("https://api.weather.gov/icons/land/day/skc?size=medium", true)]
    [InlineData("https://api.weather.gov/icons/land/night/skc?size=medium", false)]
    public void DaytimeIsReadFromTheIconPath(string icon, bool expected) =>
        Assert.Equal(expected, WeatherConditionMapper.IsDaytimeIcon(icon));
}
