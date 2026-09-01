using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PetersonCommonDataService.Models;
using PetersonCommonDataService.Services;

namespace PetersonCommonDataService.Tests;

/// <summary>
/// End-to-end checks on the wire contract the display depends on: envelope shape, and the
/// ETag/304 behaviour that makes a two-minute poll cheap.
/// </summary>
public sealed class ApiContractTests : IClassFixture<ApiContractTests.Factory>
{
    private const string TestApiKey = "contract-test-key";

    private readonly Factory _factory;

    public ApiContractTests(Factory factory) => _factory = factory;

    /// <summary>
    /// A client that always presents a valid key, so these tests exercise the wire contract
    /// rather than re-testing access control. ApiKeyTests covers that.
    /// </summary>
    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        return client;
    }

    public sealed class StubToDoistService : IToDoistService
    {
        public Task<List<ToDoistTask>> GetTasksAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ToDoistTask>
            {
                new() { Id = "1", Content = "Take out bins", SectionId = "s1", Labels = ["DakBoard"] },
            });

        public Task<List<ToDoistSection>> GetSectionsAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ToDoistSection>
            {
                new() { Id = "s1", Name = "Red", ProjectId = "p" },
            });
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Production, so the tests exercise the environment the display actually talks
            // to — including the endpoints that are meant to be Development-only.
            builder.UseEnvironment("Production");

            // Satisfy options validation without reaching any real upstream.
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ICS-URL"] = "http://localhost/unused.ics",
                ["TODOIST-API-KEY"] = "test-key",
                ["TODOIST-PROJECT-ID"] = "test-project",
                ["Api:Keys"] = TestApiKey,
            }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IToDoistService>();
                services.AddScoped<IToDoistService, StubToDoistService>();
            });

            return base.CreateHost(builder);
        }
    }

    [Fact]
    public async Task Tasks_ReturnsEnvelopeWithDataAndMeta()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/tasks");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("data", out var data));
        Assert.True(root.TryGetProperty("meta", out var meta));
        Assert.Equal("todoist", meta.GetProperty("source").GetString());
        Assert.False(meta.GetProperty("stale").GetBoolean());
        Assert.Equal("Take out bins", data[0].GetProperty("content").GetString());
        Assert.Equal("RED", data[0].GetProperty("color").GetString());
    }

    [Fact]
    public async Task Meta_CarriesNoPerRequestField_SoTheBodyIsStableBetweenPolls()
    {
        using var client = CreateAuthenticatedClient();

        var first = await client.GetStringAsync("/tasks");
        var second = await client.GetStringAsync("/tasks");

        // If this ever fails, something request-relative crept into the body and the
        // ETag - and therefore every 304 - is silently dead.
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Response_CarriesETagCacheControlAndAge()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/tasks");

        Assert.NotNull(response.Headers.ETag);
        Assert.True(response.Headers.ETag!.IsWeak);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.Age.HasValue);
    }

    [Fact]
    public async Task MatchingIfNoneMatch_Returns304WithNoBody()
    {
        using var client = CreateAuthenticatedClient();

        var first = await client.GetAsync("/tasks");
        var etag = first.Headers.ETag!.ToString();

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/tasks");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
        // Freshness must survive a 304, or the display cannot age its own data.
        Assert.True(second.Headers.Age.HasValue);
    }

    [Fact]
    public async Task NonMatchingIfNoneMatch_ReturnsFullBody()
    {
        using var client = CreateAuthenticatedClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tasks");
        request.Headers.TryAddWithoutValidation("If-None-Match", "W/\"something-else\"");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HealthLive_AnswersWithoutTouchingUpstreams()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_IsNotExposedOutsideDevelopment()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/tasks/getall");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/calendar?from=2026-09-01")]
    [InlineData("/calendar?to=2026-09-01")]
    public async Task Calendar_RejectsAHalfSpecifiedRange(string url)
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
