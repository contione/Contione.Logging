using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Contione.Logging;

internal sealed class HttpBodyLoggingMiddleware(
    RequestDelegate next,
    ILogger<HttpBodyLoggingMiddleware> logger,
    IOptionsMonitor<ContioneLoggingOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var limit = Math.Clamp(options.CurrentValue.BodyLogLimit, 1, 1_048_576);

        if (context.Request.HasJsonContentType())
        {
            context.Request.EnableBuffering();
            var buffer = new byte[limit + 1];
            var count = await context.Request.Body.ReadAtLeastAsync(
                buffer, buffer.Length, throwOnEndOfStream: false, context.RequestAborted);
            context.Request.Body.Position = 0;
            if (count > 0)
                logger.LogInformation("Request body {RequestBody}",
                    count > limit ? "[REDACTED]" : Encoding.UTF8.GetString(buffer, 0, count));
        }

        var originalBody = context.Response.Body;
        await using var capturedBody = new CapturingWriteStream(originalBody, limit);
        context.Response.Body = capturedBody;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
            if (IsJson(context.Response.ContentType) && capturedBody.Count > 0)
                logger.LogInformation("Response body {ResponseBody}",
                    capturedBody.ExceededLimit
                        ? "[REDACTED]"
                        : Encoding.UTF8.GetString(capturedBody.Content.Span));
        }
    }

    private static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var mediaType) &&
        (string.Equals(mediaType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase) ||
         mediaType.MediaType.Value?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true);
}
