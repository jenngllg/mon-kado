using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains read-only administrative report data.</summary>
[ExcludeFromCodeCoverage]
public class ReportedWishlistSummary
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
    /// <summary>Whether the wishlist is suspended.</summary>
    public bool IsSuspended
    {
        get; init;
    }
    /// <summary>The number of reports matching the reason filter.</summary>
    public int ReportCount
    {
        get; init;
    }
    /// <summary>The latest matching report date in UTC.</summary>
    public DateTime LastReportedAt
    {
        get; init;
    }
}
