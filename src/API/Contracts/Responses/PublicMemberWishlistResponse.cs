using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>Contains the public summary and active link of a discoverable list.</summary>
[ExcludeFromCodeCoverage]
public class PublicMemberWishlistResponse
{
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the list name.</summary>
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
    /// <summary>Gets the active frontend share URL, including its bearer fragment.</summary>
    public string ShareUrl { get; init; } = string.Empty;
}
