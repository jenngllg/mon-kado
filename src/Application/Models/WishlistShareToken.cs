using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents generated material for a wishlist share link.
/// </summary>
/// <param name="secret">The unprotected bearer secret.</param>
/// <param name="secretHash">The SHA-256 secret hash.</param>
/// <param name="protectedSecret">The protected secret.</param>
[ExcludeFromCodeCoverage]
public class WishlistShareToken(
    string secret,
    byte[] secretHash,
    string protectedSecret)
{
    /// <summary>
    /// Gets the unprotected bearer secret returned to the owner.
    /// </summary>
    public string Secret { get; } = secret;

    /// <summary>
    /// Gets the SHA-256 hash persisted for public verification.
    /// </summary>
    public byte[] SecretHash { get; } = secretHash;

    /// <summary>
    /// Gets the protected secret persisted for owner retrieval.
    /// </summary>
    public string ProtectedSecret { get; } = protectedSecret;
}
