using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Middleware;

/// <summary>Admits uploads after Bearer validation and before any body buffering.</summary>
/// <param name="next">The next request delegate.</param>
/// <param name="limiter">The shared upload and merchant-preview admission limiter.</param>
public class ImageProcessingLimitMiddleware(
    RequestDelegate next,
    IImageProcessingLimiter limiter)
{
    /// <summary>Holds admission until the entire upload pipeline finishes.</summary>
    /// <param name="context">The authenticated request context.</param>
    /// <returns>The completed request or a non-cacheable quota rejection.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var policyName = context.GetEndpoint()?
            .Metadata.GetMetadata<EnableRateLimitingAttribute>()?
            .PolicyName;

        if (policyName is not (AuthenticationRateLimitingExtensions.GiftImageUploadPolicy or
            AuthenticationRateLimitingExtensions.ProfileImageUploadPolicy or
            AuthenticationRateLimitingExtensions.WishImportPreviewPolicy) ||
            context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);

            return;
        }

        using var lease = await limiter.AcquireAsync(context.RequestAborted);

        if (lease is null || !lease.IsAcquired)
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
