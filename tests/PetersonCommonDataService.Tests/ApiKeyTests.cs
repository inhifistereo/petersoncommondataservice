using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PetersonCommonDataService.Services;

namespace PetersonCommonDataService.Tests;

/// <summary>
/// Access control, exercised through the real pipeline. The bypass rules matter as much as
/// the rejections: too narrow and the probes or CORS preflights break, too broad and the
/// key protects nothing.
/// </summary>
public sealed class ApiKeyTests : IClassFixture<ApiKeyTests.Factory>
{
    private const string ValidKey = "primary-key-value";
    private const string RotationKey = "secondary-key-value";

    private readonly Factory _factory;

    public ApiKeyTests(Factory factory) => _factory = factory;

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Production");

            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ICS-URL"] = "http://localhost/unused.ics",
                ["TODOIST-API-KEY"] = "test-key",
                ["TODOIST-PROJECT-ID"] = "test-project",
                ["Api:Keys"] = $"{ValidKey},{RotationKey}",
                ["Cors:AllowedOrigins:0"] = "http://display.local",
            }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IToDoistService>();
                services.AddScoped<IToDoistService, ApiContractTests.StubToDoistService>();
            });

            return base.CreateHost(builder);
        }
    }

    private static HttpRequestMessage Request(string url, string? key = null, string method = "GET")
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", key);
        }

        return request;
    }

    [Fact]
    public async Task RequestWithoutAKey_Is401ProblemJson()
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Request("/tasks"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task RequestWithAWrongKey_Is401()
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Request("/tasks", "not-the-key"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KeyThatIsAPrefixOfTheRealOne_IsRejected()
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Request("/tasks", ValidKey[..8]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidKey_IsAccepted()
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Request("/tasks", ValidKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SecondConfiguredKey_IsAlsoAccepted_SoKeysCanBeRotated()
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Request("/tasks", RotationKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_AreReachableWithoutAKey(string path)
    {
        // The Container Apps probes cannot present a key. If these ever require one the
        // platform will kill the container.
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Request(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PathMerelyStartingWithHealth_StillRequiresAKey()
    {
        // Guards against the bypass being written as a StartsWith check.
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Request("/healthful-secrets"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CorsPreflight_PassesWithoutAKey()
    {
        // A preflight carries no custom headers, so a 401 here would surface in the browser
        // as an opaque CORS failure rather than an auth error.
        using var client = _factory.CreateClient();

        var preflight = Request("/tasks", method: "OPTIONS");
        preflight.Headers.TryAddWithoutValidation("Origin", "http://display.local");
        preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(preflight);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("http://display.local", response.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task CalendarAlsoRequiresAKey()
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(Request("/calendar"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
