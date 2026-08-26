using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Combines public wishlist content with the optional current participant.
/// </summary>
/// <param name="wishlist">The public wishlist content.</param>
/// <param name="currentParticipant">The optional current participant.</param>
[ExcludeFromCodeCoverage]
public class SharedWishlistResult(
    SharedWishlistDetails wishlist,
    WishlistParticipantDetails? currentParticipant)
{
    /// <summary>Gets the public wishlist content.</summary>
    public SharedWishlistDetails Wishlist { get; } = wishlist;

    /// <summary>Gets the optional current participant.</summary>
    public WishlistParticipantDetails? CurrentParticipant { get; } = currentParticipant;
}
