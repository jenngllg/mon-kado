using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>
/// Represents the single active public share link of a wishlist.
/// </summary>
public class WishlistShareLink : IAuditableEntity
{
    private WishlistShareLink()
    {
    }

    /// <summary>
    /// Initializes a new wishlist share link.
    /// </summary>
    /// <param name="id">The share-link identifier.</param>
    /// <param name="wishlistId">The shared wishlist identifier.</param>
    /// <param name="secretHash">The SHA-256 secret hash.</param>
    /// <param name="protectedSecret">The protected secret used for owner retrieval.</param>
    public WishlistShareLink(
        Guid id,
        Guid wishlistId,
        byte[] secretHash,
        string protectedSecret)
    {
        Id = id;
        WishlistId = wishlistId;
        SecretHash = secretHash.ToArray();
        ProtectedSecret = protectedSecret;
    }

    /// <summary>
    /// Gets the share-link identifier.
    /// </summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>
    /// Gets the shared wishlist identifier.
    /// </summary>
    public Guid WishlistId
    {
        get; private set;
    }

    /// <summary>
    /// Gets the SHA-256 secret hash.
    /// </summary>
    public byte[] SecretHash { get; private set; } = [];

    /// <summary>
    /// Gets the protected secret used to reproduce the owner-facing link.
    /// </summary>
    public string ProtectedSecret { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC creation date and time.
    /// </summary>
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the optional UTC update date and time.
    /// </summary>
    public DateTime? UpdatedAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the PostgreSQL optimistic concurrency version.
    /// </summary>
    public uint Version
    {
        get; private set;
    }

    /// <summary>
    /// Replaces the bearer secret while preserving the logical share link.
    /// </summary>
    /// <param name="secretHash">The new SHA-256 secret hash.</param>
    /// <param name="protectedSecret">The new protected secret.</param>
    public void Rotate(
        byte[] secretHash,
        string protectedSecret)
    {
        SecretHash = secretHash.ToArray();
        ProtectedSecret = protectedSecret;
    }
}
