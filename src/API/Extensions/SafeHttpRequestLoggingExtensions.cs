using JennGllg.Fr.MonKado.Back.Api.Logging;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.Routing;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

/// <summary>
/// Adds query-free HTTP request completion logs.
/// </summary>
public static class SafeHttpRequestLoggingExtensions
{
    private const string OtherMethod = "Other";
    private static readonly HashSet<string> _knownMethods =
    [
        HttpMethods.Delete,
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
        HttpMethods.Patch,
        HttpMethods.Post,
        HttpMethods.Put
    ];

    /// <summary>
    /// Logs only a bounded HTTP method, the developer-defined route template and final response status.
    /// </summary>
    /// <param name="application">The application pipeline.</param>
    /// <returns>The application pipeline.</returns>
    public static IApplicationBuilder UseSafeHttpRequestLogging(
        this IApplicationBuilder application)
    {
        var logger = application.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SafeHttpRequestLoggingExtensions));
        application.Use(async (
            context,
            next) =>
        {
            var clock = context.RequestServices.GetRequiredService<TimeProvider>();
            var telemetry = context.RequestServices.GetRequiredService<IApplicationTelemetry>();
            var started = clock.GetTimestamp();
            var completed = false;
            try
            {
                await next(context);
                completed = true;
            }
            finally
            {
                var elapsed = clock.GetElapsedTime(started);
                var abandoned = context.RequestAborted.IsCancellationRequested;
                var status = completed ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
                telemetry.RecordHttp(
                    status,
                    elapsed,
                    abandoned);
                var routePattern = (context.GetEndpoint() as RouteEndpoint)?
                    .RoutePattern
                    .RawText ?? "Unmatched";
                var method = _knownMethods.Contains(context.Request.Method)
                    ? context.Request.Method
                    : OtherMethod;
                ApiLogMessages.HttpRequestCompleted(
                    logger,
                    method,
                    routePattern,
                    status,
                    elapsed.TotalMilliseconds,
                    abandoned);
            }
        });

        return application;
    }
}
