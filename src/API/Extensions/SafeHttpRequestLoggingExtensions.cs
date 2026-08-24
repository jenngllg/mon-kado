using JennGllg.Fr.MonKado.Back.Api.Logging;

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
            await next(context);
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
                context.Response.StatusCode);
        });

        return application;
    }
}
