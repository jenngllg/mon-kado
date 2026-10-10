using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Combines public wishlist content with the optional current participant.
/// </summary>
/// <param name="wishlist">The public wishlist content.</param>
/// <param name="currentParticipant">The optional current participant.</param>
/// <param name="canSubscribe">Whether the caller may explicitly follow this list.</param>
[ExcludeFromCodeCoverage]
public class SharedWishlistResult(
    SharedWishlistDetails wishlist,
    WishlistParticipantDetails? currentParticipant,
    bool canSubscribe = true)
{
    /// <summary>Gets the public wishlist content.</summary>
    public SharedWishlistDetails Wishlist { get; } = wishlist;

    /// <summary>Gets the optional current participant.</summary>
    public WishlistParticipantDetails? CurrentParticipant { get; } = currentParticipant;

    /// <summary>Gets whether the caller may explicitly follow this list.</summary>
    public bool CanSubscribe { get; } = canSubscribe;
}
