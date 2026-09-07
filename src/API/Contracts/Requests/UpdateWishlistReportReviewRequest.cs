using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Requests a report disposition and optional private note.</summary>
[ExcludeFromCodeCoverage]
public class UpdateWishlistReportReviewRequest
{
    /// <summary>Gets the required disposition: pending, upheld, or dismissed.</summary>
    public WishlistReportStatus? Status
    {
        get; init;
    }
    /// <summary>Gets the optional private note; omission clears the current note.</summary>
    public string? ReviewNote
    {
        get; init;
    }
}
