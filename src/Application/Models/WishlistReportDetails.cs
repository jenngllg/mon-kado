using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains read-only administrative report data.</summary>
[ExcludeFromCodeCoverage]
public class WishlistReportDetails
{
    /// <summary>The anonymous report identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>The report reason.</summary>
    public WishlistReportReason Reason
    {
        get; init;
    }
    /// <summary>The optional anonymous report details.</summary>
    public string? Details
    {
        get; init;
    }
    /// <summary>The UTC report date.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
}
