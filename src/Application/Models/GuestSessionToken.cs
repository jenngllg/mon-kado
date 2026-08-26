using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents generated guest-session token material.
/// </summary>
/// <param name="sessionId">The guest session identifier.</param>
/// <param name="secret">The browser secret.</param>
/// <param name="secretHash">The SHA-256 secret hash.</param>
[ExcludeFromCodeCoverage]
public class GuestSessionToken(
    Guid sessionId,
    string secret,
    byte[] secretHash)
{
    /// <summary>Gets the guest session identifier.</summary>
    public Guid SessionId { get; } = sessionId;

    /// <summary>Gets the complete opaque browser token.</summary>
    public string Secret { get; } = secret;

    /// <summary>Gets the SHA-256 hash stored by the application.</summary>
    public byte[] SecretHash { get; } = secretHash;
}
