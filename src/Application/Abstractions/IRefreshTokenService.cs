using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Creates and verifies refresh tokens.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Creates refresh token material for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The refresh token and its hash.</returns>
    RefreshToken Create(Guid sessionId);

    /// <summary>
    /// Attempts to extract a session identifier from a refresh token.
    /// </summary>
    /// <param name="value">The refresh token.</param>
    /// <param name="sessionId">The extracted session identifier.</param>
    /// <returns><see langword="true" /> when the token has a valid format.</returns>
    bool TryGetSessionId(
        string value,
        out Guid sessionId);

    /// <summary>
    /// Verifies a refresh token against a stored hash.
    /// </summary>
    /// <param name="value">The refresh token.</param>
    /// <param name="expectedHash">The expected hash.</param>
    /// <returns><see langword="true" /> when the token matches the hash.</returns>
    bool Verify(
        string value,
        byte[] expectedHash);
}
