using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;

/// <summary>
/// Represents the source labels retained by reservation history.
/// </summary>
/// <param name="wishlistName">The wishlist name.</param>
/// <param name="wishName">The gift-wish name.</param>
[ExcludeFromCodeCoverage]
public class GiftReservationHistorySource(
    string wishlistName,
    string wishName)
{
    /// <summary>Gets the wishlist name.</summary>
    public string WishlistName { get; } = wishlistName;

    /// <summary>Gets the gift-wish name.</summary>
    public string WishName { get; } = wishName;
}
