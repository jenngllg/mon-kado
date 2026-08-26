using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Creates and validates opaque guest-session tokens.
/// </summary>
public interface IGuestSessionTokenService
{
    /// <summary>Creates token material for a guest session.</summary>
    /// <param name="sessionId">The generated guest session identifier.</param>
    /// <returns>The generated token material.</returns>
    GuestSessionToken Create(Guid sessionId);

    /// <summary>Parses an opaque guest-session token.</summary>
    /// <param name="token">The presented token.</param>
    /// <param name="sessionId">The parsed session identifier.</param>
    /// <param name="secretHash">The SHA-256 hash of the parsed secret.</param>
    /// <returns><see langword="true" /> when the token is well formed.</returns>
    bool TryParse(
        string token,
        out Guid sessionId,
        out byte[] secretHash);

    /// <summary>Compares a presented hash with a persisted hash in constant time.</summary>
    /// <param name="presentedHash">The presented token hash.</param>
    /// <param name="persistedHash">The persisted token hash.</param>
    /// <returns><see langword="true" /> when the hashes match.</returns>
    bool Verify(
        byte[] presentedHash,
        byte[] persistedHash);
}
