using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.Authorization;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Authorization;

/// <summary>Authorizes subscription ownership without revealing foreign resources.</summary>
/// <param name="subscriptions">The scoped subscription service.</param>
/// <param name="httpContextAccessor">The request context.</param>
public class WishlistSubscriptionOwnerAuthorizationHandler(
    IWishlistSubscriptionService subscriptions,
    IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<WishlistSubscriptionOwnerRequirement, Guid>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WishlistSubscriptionOwnerRequirement requirement,
        Guid resource)
    {

        if (!Guid.TryParse(
            context.User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var memberId) || memberId == Guid.Empty)
            throw new InvalidAuthenticationSessionException();

        var subscription = await subscriptions.GetAsync(
            memberId,
            resource,
            httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        if (subscription is not null)
            context.Succeed(requirement);
    }
}
