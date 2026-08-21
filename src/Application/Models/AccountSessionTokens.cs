using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the tokens created for an authentication session.
/// </summary>
/// <param name="accessToken">The access token.</param>
/// <param name="refreshToken">The refresh token.</param>
/// <param name="refreshTokenExpiresAt">The refresh token expiration.</param>
/// <param name="isPersistent">Whether the browser cookie is persistent.</param>
[ExcludeFromCodeCoverage]
public class AccountSessionTokens(
    AccessToken accessToken,
    string refreshToken,
    DateTime refreshTokenExpiresAt,
    bool isPersistent)
{
    /// <summary>
    /// Gets the access token.
    /// </summary>
    public AccessToken AccessToken { get; } = accessToken;

    /// <summary>
    /// Gets the refresh token.
    /// </summary>
    public string RefreshToken { get; } = refreshToken;

    /// <summary>
    /// Gets the refresh token expiration.
    /// </summary>
    public DateTime RefreshTokenExpiresAt { get; } = refreshTokenExpiresAt;

    /// <summary>
    /// Gets whether the browser cookie is persistent.
    /// </summary>
    public bool IsPersistent { get; } = isPersistent;
}
