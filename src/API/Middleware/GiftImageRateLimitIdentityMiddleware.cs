using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Middleware;

/// <summary>
/// Authenticates gift-image upload callers before their member-scoped rate limit is evaluated.
/// </summary>
/// <param name="next">The next request delegate.</param>
public class GiftImageRateLimitIdentityMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Authenticates upload requests without invoking remote authentication request handlers.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var policyName = context
            .GetEndpoint()?
            .Metadata
            .GetMetadata<EnableRateLimitingAttribute>()?
            .PolicyName;

        if (!string.Equals(
                policyName,
                AuthenticationRateLimitingExtensions.GiftImageUploadPolicy,
                StringComparison.Ordinal))
        {
            await next(context);

            return;
        }

        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

        if (result.Principal is not null)
            context.User = result.Principal;

        await next(context);
    }
}
