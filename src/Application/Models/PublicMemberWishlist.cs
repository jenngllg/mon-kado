using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains a discoverable list and the active bearer link needed by the shared journey.</summary>
[ExcludeFromCodeCoverage]
public class PublicMemberWishlist
{
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the public list name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the occasion.</summary>
    public WishlistOccasion Occasion
    {
        get; init;
    }
    /// <summary>Gets the optional event date.</summary>
    public DateOnly? EventDate
    {
        get; init;
    }
    /// <summary>Gets the active share-link identifier.</summary>
    public Guid ShareLinkId
    {
        get; init;
    }
    /// <summary>Gets the bearer secret, which must never be logged.</summary>
    public string Secret { get; init; } = string.Empty;
}
