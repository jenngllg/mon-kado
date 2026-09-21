using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Logging;

using System.Globalization;
using System.Threading.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

/// <summary>Writes the common perimeter and business-quota error contract.</summary>
public static class RateLimitResponseExtensions
{
    /// <summary>Writes a non-cacheable bounded error without recording caller data.</summary>
    /// <param name="context">The rejected request.</param>
    /// <param name="lease">The rejected quota lease.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The asynchronous response write.</returns>
    public static async Task WriteRejectionAsync(
        HttpContext context,
        RateLimitLease lease,
        CancellationToken cancellationToken)
    {
        var retryAfter = TimeSpan.FromMinutes(1);

        if (lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out var metadata))
            retryAfter = metadata;

        context.Response.Headers.RetryAfter = Math.Max(
                1,
                (int)Math.Ceiling(retryAfter.TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);
        context.Response.Headers.CacheControl = "no-store";
        var error = new ErrorResponse(
            StatusCodes.Status429TooManyRequests,
            "Rate limit exceeded",
            "Too many requests. Retry later.",
            ErrorCodes.RequestRateLimitExceeded,
            null);
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RateLimitResponseExtensions));
        ApiLogMessages.ExpectedHttpError(
            logger,
            error.StatusCode,
            ErrorCodes.RequestRateLimitExceeded);
        context.Response.StatusCode = error.StatusCode;
        await context.Response.WriteAsJsonAsync(
            error,
            cancellationToken);
    }
}
