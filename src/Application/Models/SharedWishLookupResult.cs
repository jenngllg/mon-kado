using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the outcome of resolving a gift wish through a share link.
/// </summary>
/// <param name="outcome">The lookup outcome.</param>
/// <param name="wishlistId">The resolved wishlist identifier.</param>
/// <param name="wish">The resolved gift-wish details.</param>
[ExcludeFromCodeCoverage]
public class SharedWishLookupResult(
    SharedWishLookupOutcome outcome,
    Guid? wishlistId,
    SharedWishDetail? wish)
{
    /// <summary>Gets the lookup outcome.</summary>
    public SharedWishLookupOutcome Outcome { get; } = outcome;

    /// <summary>Gets the resolved wishlist identifier.</summary>
    public Guid? WishlistId { get; } = wishlistId;

    /// <summary>Gets the resolved gift-wish details.</summary>
    public SharedWishDetail? Wish { get; } = wish;
}
