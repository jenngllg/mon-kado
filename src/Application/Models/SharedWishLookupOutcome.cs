namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Identifies the outcome of resolving a gift wish through a share link.
/// </summary>
public enum SharedWishLookupOutcome
{
    /// <summary>The shared gift wish was found.</summary>
    Found,

    /// <summary>The share link or its secret is unavailable.</summary>
    SharedWishlistNotFound,

    /// <summary>The gift wish is unavailable under the shared wishlist.</summary>
    WishNotFound
}
