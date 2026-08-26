using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents an owner-facing wishlist share link.
/// </summary>
/// <param name="id">The share-link identifier.</param>
/// <param name="wishlistId">The shared wishlist identifier.</param>
/// <param name="secret">The bearer secret.</param>
/// <param name="createdAt">The UTC creation date and time.</param>
/// <param name="updatedAt">The optional UTC update date and time.</param>
/// <param name="version">The optimistic concurrency version.</param>
[ExcludeFromCodeCoverage]
public class WishlistShareLinkDetails(
    Guid id,
    Guid wishlistId,
    string secret,
    DateTime createdAt,
    DateTime? updatedAt,
    uint version)
{
    /// <summary>Gets the share-link identifier.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the shared wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the bearer secret.</summary>
    public string Secret { get; } = secret;
    /// <summary>Gets the UTC creation date and time.</summary>
    public DateTime CreatedAt { get; } = createdAt;
    /// <summary>Gets the optional UTC update date and time.</summary>
    public DateTime? UpdatedAt { get; } = updatedAt;
    /// <summary>Gets the optimistic concurrency version.</summary>
    public uint Version { get; } = version;
}
