using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Selects reports by disposition without treating all as a persisted state.</summary>
public enum WishlistReportStatusFilter
{
    /// <summary>Selects pending reports.</summary>
    Pending = (int)WishlistReportStatus.Pending,
    /// <summary>Selects upheld reports.</summary>
    Upheld = (int)WishlistReportStatus.Upheld,
    /// <summary>Selects dismissed reports.</summary>
    Dismissed = (int)WishlistReportStatus.Dismissed,
    /// <summary>Selects reports in every state.</summary>
    All = -1
}
