using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains read-only administrative report data.</summary>
[ExcludeFromCodeCoverage]
public class WishlistReportDetails
{
    /// <summary>Gets the current disposition.</summary>
    public WishlistReportStatus Status
    {
        get; init;
    }
    /// <summary>Gets the latest private review note.</summary>
    public string? ReviewNote
    {
        get; init;
    }
    /// <summary>Gets the latest UTC review date.</summary>
    public DateTime? ReviewedAt
    {
        get; init;
    }
    /// <summary>Gets the latest deciding administrator, or null after account deletion.</summary>
    public Guid? ReviewedByAdministratorId
    {
        get; init;
    }
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
