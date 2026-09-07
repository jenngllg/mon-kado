using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Combines existing wishlist metadata with the administrative owner and gift projections.</summary>
/// <param name="wishlist">The current wishlist metadata and private moderation state.</param>
/// <param name="ownerId">The owner identifier.</param>
/// <param name="ownerDisplayName">The current owner display name.</param>
/// <param name="wishes">The current ordered gifts without reservation data.</param>
[ExcludeFromCodeCoverage]
public class ReportedWishlistDetails(
    WishlistDetails wishlist,
    Guid ownerId,
    string ownerDisplayName,
    IReadOnlyCollection<ReportedWishDetails> wishes)
{
    /// <summary>Gets the current wishlist metadata.</summary>
    public WishlistDetails Wishlist { get; } = wishlist;

    /// <summary>Gets the owner identifier.</summary>
    public Guid OwnerId { get; } = ownerId;

    /// <summary>Gets the current owner display name.</summary>
    public string OwnerDisplayName { get; } = ownerDisplayName;

    /// <summary>Gets the ordered current gifts.</summary>
    public IReadOnlyCollection<ReportedWishDetails> Wishes { get; } = wishes;
}
