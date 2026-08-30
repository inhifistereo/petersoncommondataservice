using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PetersonCommonDataService.Errors;

/// <summary>
/// Turns unhandled exceptions into RFC 9457 ProblemDetails responses.
/// Upstream failures become 502 (naming the dependency and its status) rather than an
/// opaque 500 — a bad Todoist token previously surfaced as a bodiless 500 and took
/// three rounds of log-diving to identify.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = Translate(exception, httpContext, out var logLevel);

        logger.Log(
            logLevel,
            exception,
            "Request {Method} {Path} failed with {StatusCode}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            problem.Status);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private static ProblemDetails Translate(Exception exception, HttpContext context, out LogLevel logLevel)
    {
        switch (exception)
        {
            case UpstreamException upstream:
                logLevel = LogLevel.Warning;
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status502BadGateway,
                    Title = "Upstream dependency failed",
                    Detail = $"The '{upstream.UpstreamName}' dependency could not be reached or returned an error.",
                    Instance = context.Request.Path,
                };
                problem.Extensions["upstream"] = upstream.UpstreamName;
                if (upstream.UpstreamStatus is not null)
                {
                    problem.Extensions["upstreamStatus"] = (int)upstream.UpstreamStatus.Value;
                }
                return problem;

            case TaskCanceledException or TimeoutException:
                logLevel = LogLevel.Warning;
                return new ProblemDetails
                {
                    Status = StatusCodes.Status504GatewayTimeout,
                    Title = "Upstream dependency timed out",
                    Instance = context.Request.Path,
                };

            case ArgumentException or FormatException:
                logLevel = LogLevel.Information;
                return new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid request",
                    Detail = exception.Message,
                    Instance = context.Request.Path,
                };

            default:
                logLevel = LogLevel.Error;
                // No Detail: never leak internal exception text to a caller.
                return new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred",
                    Instance = context.Request.Path,
                };
        }
    }
}
