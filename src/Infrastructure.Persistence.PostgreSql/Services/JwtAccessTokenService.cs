using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates signed JWT access tokens for authenticated members.
/// </summary>
/// <param name="options">The JWT options.</param>
/// <param name="timeProvider">The time provider.</param>
internal class JwtAccessTokenService(
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IAccessTokenService
{
    internal const int LifetimeSeconds = 15 * 60;

    private readonly JwtOptions _options = options.Value;
    private readonly byte[] _signingKey = Convert.FromBase64String(options.Value.SigningKey);

    /// <summary>
    /// Creates a signed access token for a member.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <returns>The signed access token.</returns>
    public AccessToken Create(Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                userId.ToString("D")),
            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.CreateVersion7(now.UtcDateTime).ToString("N")),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(_signingKey),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            notBefore: null,
            expires: now.AddSeconds(LifetimeSeconds).UtcDateTime,
            signingCredentials);
        var value = new JwtSecurityTokenHandler().WriteToken(token);

        return new AccessToken(
            value,
            LifetimeSeconds);
    }
}
