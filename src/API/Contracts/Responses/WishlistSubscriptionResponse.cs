using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>Represents one accessible followed list.</summary>
[ExcludeFromCodeCoverage]
public class WishlistSubscriptionResponse
{
    /// <summary>Gets the subscription identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }
    /// <summary>Gets the current list name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the author's display name.</summary>
    public string OwnerDisplayName { get; init; } = string.Empty;
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
    /// <summary>Gets the UTC subscription creation time.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
    /// <summary>Gets the shared-list URL with its secret in the fragment.</summary>
    public string ShareUrl { get; init; } = string.Empty;
}
