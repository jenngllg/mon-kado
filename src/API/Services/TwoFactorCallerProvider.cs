using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>Exposes only identifiers from the already validated Bearer principal.</summary>
/// <param name="httpContextAccessor">The current request context.</param>
public class TwoFactorCallerProvider(IHttpContextAccessor httpContextAccessor) : ITwoFactorCallerProvider
{
    /// <inheritdoc/>
    public TwoFactorCaller? GetCurrent()
    {
        var principal = httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out var memberId) ||
            !Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Jti),
                out var tokenId))
            return null;

        return new TwoFactorCaller { MemberId = memberId, AccessTokenId = tokenId };
    }
}
