using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains private administrative report review data.</summary>
[ExcludeFromCodeCoverage]
public class WishlistReportReviewEventDetails
{
    /// <summary>The review event identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>The previous disposition.</summary>
    public WishlistReportStatus PreviousStatus
    {
        get; init;
    }
    /// <summary>The new disposition.</summary>
    public WishlistReportStatus Status
    {
        get; init;
    }
    /// <summary>The optional private note.</summary>
    public string? Note
    {
        get; init;
    }
    /// <summary>The administrator identifier, or null after deletion.</summary>
    public Guid? AdministratorId
    {
        get; init;
    }
    /// <summary>The UTC review date.</summary>
    public DateTime OccurredAt
    {
        get; init;
    }
}
