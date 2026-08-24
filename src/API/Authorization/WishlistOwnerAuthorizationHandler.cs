using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authorization;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Authorization;

/// <summary>
/// Authorizes owner access to private wishlists without revealing their existence.
/// </summary>
/// <param name="wishlistService">The wishlist service.</param>
/// <param name="httpContextAccessor">The current HTTP context accessor.</param>
public class WishlistOwnerAuthorizationHandler(
    IWishlistService wishlistService,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<WishlistOwnerRequirement, Guid>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WishlistOwnerRequirement requirement,
        Guid resource)
    {
        var subject = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(
            subject,
            out var memberId) || memberId == Guid.Empty)
        {
            throw new InvalidAuthenticationSessionException();
        }

        var access = await wishlistService.GetAccessAsync(
            memberId,
            resource,
            httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is WishlistAccess.Owner)
            context.Succeed(requirement);
    }
}
