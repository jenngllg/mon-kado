using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Extensions;

namespace JennGllg.Fr.MonKado.Back.Api.Middleware;

/// <summary>Rejects excess traffic before any authentication or database access.</summary>
/// <param name="next">The subsequent middleware.</param>
/// <param name="limiter">The process-wide perimeter limiter.</param>
public class GeneralRateLimitMiddleware(
    RequestDelegate next,
    IGeneralRequestLimiter limiter)
{
    /// <summary>Applies the perimeter quota only to API and CSRF-token requests.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>The asynchronous pipeline operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {

        if (HttpMethods.IsOptions(context.Request.Method)
            || (!context.Request.Path.StartsWithSegments("/api/v1")
                && !context.Request.Path.StartsWithSegments("/security/csrf-token")))
        {
            await next(context);

            return;
        }

        using var lease = limiter.AttemptAcquire(context);

        if (!lease.IsAcquired)
        {
            await RateLimitResponseExtensions.WriteRejectionAsync(
                context,
                lease,
                context.RequestAborted);

            return;
        }

        await next(context);
    }
}
