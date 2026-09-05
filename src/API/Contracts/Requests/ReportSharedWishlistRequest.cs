using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents an anonymous shared-wishlist report request.
/// </summary>
/// <param name="reason">The report reason.</param>
/// <param name="details">The optional report details.</param>
[ExcludeFromCodeCoverage]
public class ReportSharedWishlistRequest(
    WishlistReportReason? reason,
    string? details)
{
    /// <summary>Gets the report reason.</summary>
    public WishlistReportReason? Reason { get; } = reason;

    /// <summary>Gets the optional report details.</summary>
    public string? Details { get; } = details;
}
