using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents an owner-facing wishlist share link.
/// </summary>
/// <param name="id">The share-link identifier.</param>
/// <param name="shareUrl">The copyable frontend URL.</param>
/// <param name="createdAt">The UTC creation date and time.</param>
/// <param name="updatedAt">The optional UTC rotation date and time.</param>
[ExcludeFromCodeCoverage]
public class WishlistShareLinkResponse(
    Guid id,
    string shareUrl,
    DateTime createdAt,
    DateTime? updatedAt)
{
    /// <summary>Gets the share-link identifier.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the copyable frontend URL.</summary>
    public string ShareUrl { get; } = shareUrl;
    /// <summary>Gets the UTC creation date and time.</summary>
    public DateTime CreatedAt { get; } = createdAt;
    /// <summary>Gets the optional UTC rotation date and time.</summary>
    public DateTime? UpdatedAt { get; } = updatedAt;
}
