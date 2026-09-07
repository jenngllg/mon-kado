namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Selects reports by disposition without treating all as a persisted state.</summary>
public enum WishlistReportStatusFilter
{
    /// <summary>Selects pending reports.</summary>
    Pending = 0,
    /// <summary>Selects upheld reports.</summary>
    Upheld = 1,
    /// <summary>Selects dismissed reports.</summary>
    Dismissed = 2,
    /// <summary>Selects reports in every state.</summary>
    All = -1
}
