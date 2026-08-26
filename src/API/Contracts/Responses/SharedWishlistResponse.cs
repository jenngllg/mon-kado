using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents a wishlist exposed through a share link.
/// </summary>
[ExcludeFromCodeCoverage]
public class SharedWishlistResponse
{
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the owner display name.</summary>
    public string OwnerDisplayName
    {
        get; init;
    } = string.Empty;
    /// <summary>Gets the wishlist name.</summary>
    public string Name
    {
        get; init;
    } = string.Empty;
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
    /// <summary>Gets the optional owner message.</summary>
    public string? Message
    {
        get; init;
    }
    /// <summary>Gets the ordered public gift wishes.</summary>
    public IReadOnlyCollection<SharedWishResponse> Wishes
    {
        get; init;
    } = [];

    /// <summary>Gets the optional participant associated with the current caller.</summary>
    public WishlistParticipantResponse? CurrentParticipant
    {
        get; init;
    }
}
