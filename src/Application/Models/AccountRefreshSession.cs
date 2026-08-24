using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents refresh-only material created for a browser session.
/// </summary>
/// <param name="refreshToken">The refresh token value.</param>
/// <param name="refreshTokenExpiresAt">The refresh token expiration.</param>
/// <param name="isPersistent">Whether the browser cookie is persistent.</param>
[ExcludeFromCodeCoverage]
public class AccountRefreshSession(
    string refreshToken,
    DateTime refreshTokenExpiresAt,
    bool isPersistent)
{
    /// <summary>
    /// Gets the refresh token value.
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
