using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetersonCommonDataService.Configuration;

namespace PetersonCommonDataService.Security;

/// <summary>
/// Requires a shared key in the <c>X-Api-Key</c> header on everything except health
/// endpoints and CORS preflights.
/// </summary>
/// <remarks>
/// Scope check: if the display is a browser loading static JavaScript, this key is visible
/// in view-source to anyone who can reach that page. It keeps crawlers, scanners and
/// passers-by off an otherwise open endpoint and protects the Todoist rate limit. It is
/// not authentication, and should not be relied on as if it were.
/// </remarks>
public sealed class ApiKeyMiddleware(
    RequestDelegate next,
    IOptions<ApiOptions> options,
    ILogger<ApiKeyMiddleware> logger)
{
    public const string HeaderName = "X-Api-Key";

    /// <summary>
    /// Exact paths served without a key. Matched exactly rather than by prefix, because a
    /// StartsWith("/health") check would also expose anything named "/healthcheck-data".
    /// </summary>
    private static readonly HashSet<string> AnonymousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health/live",
        "/health/ready",
    };

    private readonly ApiOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExempt(context))
        {
            await next(context);
            return;
        }

        if (!_options.HasKeys)
        {
            // Startup validation forbids this outside Development, so reaching here means
            // a developer running locally without a key configured.
            logger.LogWarning(
                "No API keys configured; allowing {Method} {Path} unauthenticated. Startup validation forbids this outside Development",
                context.Request.Method, context.Request.Path);
            await next(context);
            return;
        }

        var presented = context.Request.Headers[HeaderName].ToString();
        if (!string.IsNullOrEmpty(presented) && IsValid(presented))
        {
            await next(context);
            return;
        }

        // Log that it failed and from where, never what was presented.
        logger.LogWarning(
            "Rejected {Method} {Path} from {RemoteIp}: {Reason}",
            context.Request.Method,
            context.Request.Path,
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            string.IsNullOrEmpty(presented) ? "no API key" : "invalid API key");

        await WriteUnauthorizedAsync(context);
    }

    private static bool IsExempt(HttpContext context)
    {
        // A CORS preflight carries no custom headers by definition, so rejecting OPTIONS
        // would make every cross-origin request fail with an opaque CORS error rather than
        // a clear 401.
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            return true;
        }

        var path = context.Request.Path.Value;
        return path is not null && AnonymousPaths.Contains(path.TrimEnd('/'));
    }

    private bool IsValid(string presented)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var valid = false;

        foreach (var key in _options.ParsedKeys)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);

            // FixedTimeEquals requires equal lengths, and the length comparison itself is
            // not secret. Every candidate is checked so the work does not depend on which
            // key matched. No early exit.
            if (presentedBytes.Length == keyBytes.Length &&
                CryptographicOperations.FixedTimeEquals(presentedBytes, keyBytes))
            {
                valid = true;
            }
        }

        return valid;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Missing or invalid API key",
            Detail = $"Supply a valid key in the {HeaderName} header.",
            Instance = context.Request.Path,
        };

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        // The contentType argument is required: WriteAsJsonAsync otherwise resets the
        // header to application/json and the response stops being a ProblemDetails.
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json");
    }
}
