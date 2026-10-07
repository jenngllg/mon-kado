using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Middleware;

/// <summary>Bounds native image processing before uploads are buffered or public previews are rendered.</summary>
/// <param name="next">The next request delegate.</param>
/// <param name="limiter">The shared upload, merchant-import and public-preview admission limiter.</param>
public class ImageProcessingLimitMiddleware(
    RequestDelegate next,
    IImageProcessingLimiter limiter)
{
    /// <summary>Holds shared admission until the entire image-processing pipeline finishes.</summary>
    /// <param name="context">The public rendering or authenticated upload request context.</param>
    /// <returns>The completed request or a non-cacheable quota rejection.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var policyName = endpoint?
            .Metadata.GetMetadata<EnableRateLimitingAttribute>()?
            .PolicyName;
        var isPublicRendering = endpoint?
            .Metadata.GetMetadata<PublicImageProcessingAttribute>() is not null;
        var isAuthenticatedUpload = policyName is (
            AuthenticationRateLimitingExtensions.GiftImageUploadPolicy or
            AuthenticationRateLimitingExtensions.ProfileImageUploadPolicy or
            AuthenticationRateLimitingExtensions.WishImportPreviewPolicy) &&
            context.User.Identity?.IsAuthenticated == true;

        if (!isPublicRendering && !isAuthenticatedUpload)
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
