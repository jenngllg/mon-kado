using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains an accessible subscription summary, without wishes or reservations.</summary>
[ExcludeFromCodeCoverage]
public class WishlistSubscriptionDetails
{
    /// <summary>Gets the subscription identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the shared wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }
    /// <summary>Gets the current list name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the list owner's public display name.</summary>
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
    /// <summary>Gets when the member subscribed.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
    /// <summary>Gets the accepted share-link identifier.</summary>
    public Guid ShareLinkId
    {
        get; init;
    }
    /// <summary>Gets the protected secret for trusted URL construction only.</summary>
    public string ProtectedSecret { get; init; } = string.Empty;
}
