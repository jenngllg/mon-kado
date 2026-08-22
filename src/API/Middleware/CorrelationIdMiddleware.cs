using System.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Api.Middleware;
/// <summary>
/// Represents correlation id middleware.
/// </summary>
/// <param name="next">The next.</param>
/// <param name="logger">The logger.</param>

public class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    /// <summary>
    /// Identifies header name.
    /// </summary>
    public const string HeaderName = "X-Correlation-ID";
    /// <summary>
    /// Executes the invoke async operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.Headers[HeaderName] = traceId;

        using (logger.BeginScope(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["CorrelationId"] = traceId,
            ["TraceId"] = traceId
        }))
        {
            await next(context);
        }
    }
}
