using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PetersonCommonDataService.Caching;
using PetersonCommonDataService.Configuration;
using PetersonCommonDataService.Errors;
using PetersonCommonDataService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://*:8080");

if (builder.Environment.IsDevelopment())
{
    // Populates process environment variables from .env; the extra
    // AddEnvironmentVariables() below re-reads them into configuration.
    DotNetEnv.Env.Load();
    builder.Configuration.AddEnvironmentVariables();
}

// ---------------------------------------------------------------------------
// Options
//
// The deployment injects secrets as flat, dash-named environment variables
// (ICS-URL, TODOIST-API-KEY, TODOIST-PROJECT-ID) via Container App secrets.
// Those names are kept so infrastructure keeps working, but they are bound onto
// real options objects here so the rest of the app never reads raw config keys.
// ---------------------------------------------------------------------------
builder.Services.AddOptions<CalendarOptions>()
    .Bind(builder.Configuration.GetSection(CalendarOptions.SectionName))
    .PostConfigure(options =>
    {
        var icsUrl = builder.Configuration["ICS-URL"];
        if (!string.IsNullOrWhiteSpace(icsUrl))
        {
            options.IcsUrl = icsUrl;
        }
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<TodoistOptions>()
    .Bind(builder.Configuration.GetSection(TodoistOptions.SectionName))
    .PostConfigure(options =>
    {
        var apiKey = builder.Configuration["TODOIST-API-KEY"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            options.ApiKey = apiKey;
        }

        var projectId = builder.Configuration["TODOIST-PROJECT-ID"];
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            options.ProjectId = projectId;
        }
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));

// ---------------------------------------------------------------------------
// HTTP clients — every one gets an explicit timeout. Without it the default is
// 100 seconds, long enough for one hung upstream to stall the display's poll.
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient<CalendarService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient<IToDoistService, ToDoistService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<TodoistOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
});

// ---------------------------------------------------------------------------
// MVC + serialisation. One JSON configuration for every endpoint, so controllers
// return typed objects and never hand-serialise.
// ---------------------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(jsonOptions =>
    {
        jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        jsonOptions.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        jsonOptions.JsonSerializerOptions.WriteIndented = builder.Environment.IsDevelopment();
    });

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IcsEventExpander>();

// Caching. The cache is in-process, so it dies with the replica — acceptable because the
// display's frequent polling keeps a replica alive, and a cold start simply refetches.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICachedSource, CachedSource>();
builder.Services.AddScoped<DisplayTaskService>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Two health endpoints with different jobs:
//   /health/live  — zero checks, proves only that the process answers. This is what
//                   the Container Apps liveness and startup probes target. It must
//                   never depend on an upstream, or Todoist going down would restart
//                   the container and destroy the cache.
//   /health/ready — reserved for upstream freshness reporting; wired to no probe.
builder.Services.AddHealthChecks();

builder.Services.AddCors(corsOptions =>
{
    var allowedOrigins = builder.Configuration
        .GetSection($"{CorsOptions.SectionName}:AllowedOrigins")
        .Get<string[]>() ?? [];

    corsOptions.AddPolicy("DisplayOrigins", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var app = builder.Build();

app.UseExceptionHandler();

app.UseRouting();
app.UseCors("DisplayOrigins");

// After CORS so preflights are answered without buffering, and before the endpoints
// whose responses it validates.
app.UseMiddleware<ConditionalGetMiddleware>();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready");

// Retained so the existing Container Apps probes keep passing until Terraform
// is updated to point at /health/live.
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});

app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can construct the app via WebApplicationFactory.</summary>
public partial class Program;
