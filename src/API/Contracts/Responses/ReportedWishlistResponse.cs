using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>Contains current administrator-only wishlist content without participant or share data.</summary>
[ExcludeFromCodeCoverage]
public class ReportedWishlistResponse
{
    /// <summary>The wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }
    /// <summary>The current wishlist name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>The owner identifier.</summary>
    public Guid OwnerId
    {
        get; init;
    }
    /// <summary>The current owner display name.</summary>
    public string OwnerDisplayName { get; init; } = string.Empty;
    /// <summary>The wishlist occasion.</summary>
    public WishlistOccasion Occasion
    {
        get; init;
    }
    /// <summary>The optional event date.</summary>
    public DateOnly? EventDate
    {
        get; init;
    }
    /// <summary>The optional owner message.</summary>
    public string? Message
    {
        get; init;
    }
    /// <summary>The UTC creation date.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
    /// <summary>The UTC modification date.</summary>
    public DateTime? UpdatedAt
    {
        get; init;
    }
    /// <summary>Whether the wishlist is suspended.</summary>
    public bool IsSuspended
    {
        get; init;
    }
    /// <summary>The private suspension reason.</summary>
    public string? SuspensionReason
    {
        get; init;
    }
    /// <summary>The UTC suspension date.</summary>
    public DateTime? SuspendedAt
    {
        get; init;
    }
    /// <summary>The current ordered wishes.</summary>
    public IEnumerable<ReportedWishResponse> Wishes { get; init; } = [];
}
