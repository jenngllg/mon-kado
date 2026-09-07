using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authorization;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Authorization;

/// <summary>Authorizes administrators using PostgreSQL, never JWT role or profile claims.</summary>
/// <param name="accessService">The current administrator access service.</param>
/// <param name="httpContextAccessor">The HTTP context accessor.</param>
public class AdministratorAuthorizationHandler(
    IAdministratorAccessService accessService,
    IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<AdministratorRequirement>
{
    /// <inheritdoc/>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdministratorRequirement requirement)
    {
        var subject = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(
            subject,
            out var memberId) || memberId == Guid.Empty)
            throw new InvalidAuthenticationSessionException();
        var access = await accessService.GetAccessAsync(
            memberId,
            httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        if (access is AdministratorAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not AdministratorAccess.Granted)
            throw new AdministratorAccessDeniedException();
        context.Succeed(requirement);
    }
}
