using Microsoft.AspNetCore.Mvc;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Models;
using PetersonCommonDataService.Services.Weather;

namespace PetersonCommonDataService.Controllers;

[ApiController]
[Route("weather")]
public sealed class WeatherController(
    WeatherService weatherService,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Current conditions, daily and hourly forecast, and any active alerts.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<WeatherSnapshot>>> GetWeather(CancellationToken cancellationToken)
    {
        if (!weatherService.IsConfigured)
        {
            // Weather is optional: an unset location disables this endpoint rather than
            // preventing the whole service from starting.
            return Problem(
                title: "Weather is not configured",
                detail: "Set Weather:Latitude and Weather:Longitude to enable this endpoint.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var cached = await weatherService.GetWeatherAsync(cancellationToken);

        var meta = new ResponseMeta
        {
            Source = "nws",
            FetchedAt = cached.FetchedAt,
            Stale = cached.Stale,
            StaleReason = cached.StaleReason,
            TtlSeconds = cached.TtlSeconds,
        };

        Response.ApplyFreshness(meta, timeProvider);

        return Ok(new ApiResponse<WeatherSnapshot>(cached.Value, meta));
    }
}
