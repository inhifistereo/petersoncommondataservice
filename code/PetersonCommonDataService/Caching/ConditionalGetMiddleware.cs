using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace PetersonCommonDataService.Caching;

/// <summary>
/// Adds an ETag to successful GET responses and answers matching If-None-Match with 304.
/// </summary>
/// <remarks>
/// The display polls every couple of minutes and its data usually has not changed, so
/// most of those polls should cost headers rather than a payload. This works only because
/// response bodies carry no per-request values — see <c>ApiResponse</c>.
/// <para>
/// Bodies are buffered to hash them. That is acceptable here because payloads are a few
/// kilobytes; it would not be for large or streamed responses.
/// </para>
/// </remarks>
public sealed class ConditionalGetMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            // Only cache-validate plain successes. Errors and 304s pass through untouched.
            if (context.Response.StatusCode != StatusCodes.Status200OK)
            {
                buffer.Position = 0;
                context.Response.Body = originalBody;
                await buffer.CopyToAsync(originalBody, context.RequestAborted);
                return;
            }

            var etag = ComputeETag(buffer);
            context.Response.Headers.ETag = etag;

            if (IsNotModified(context.Request, etag))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = null;
                context.Response.Body = originalBody;
                return;
            }

            buffer.Position = 0;
            context.Response.ContentLength = buffer.Length;
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static string ComputeETag(MemoryStream buffer)
    {
        buffer.Position = 0;
        var hash = SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        // Weak: this asserts semantic equivalence, not byte-identical transfer encoding.
        return $"W/\"{Convert.ToHexStringLower(hash.AsSpan(0, 16))}\"";
    }

    private static bool IsNotModified(HttpRequest request, string etag)
    {
        var header = request.Headers[HeaderNames.IfNoneMatch];
        if (header.Count == 0)
        {
            return false;
        }

        foreach (var value in header)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (value == "*")
            {
                return true;
            }

            foreach (var candidate in value.Split(','))
            {
                if (string.Equals(candidate.Trim(), etag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
