using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Creates, protects and verifies wishlist share-link secrets.
/// </summary>
public interface IWishlistShareTokenService
{
    /// <summary>
    /// Creates new cryptographic share material.
    /// </summary>
    /// <returns>The generated token material.</returns>
    WishlistShareToken Create();

    /// <summary>
    /// Restores a protected secret for its owner.
    /// </summary>
    /// <param name="protectedSecret">The protected secret.</param>
    /// <returns>The original Base64Url secret.</returns>
    string Unprotect(string protectedSecret);

    /// <summary>
    /// Verifies a presented secret against its stored hash.
    /// </summary>
    /// <param name="secret">The presented Base64Url secret.</param>
    /// <param name="expectedHash">The stored SHA-256 hash.</param>
    /// <returns><see langword="true" /> when the secret is valid.</returns>
    bool Verify(
        string secret,
        byte[] expectedHash);
}
