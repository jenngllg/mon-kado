namespace JennGllg.Fr.MonKado.Back.Domain.Enums;

/// <summary>Identifies the current administrative disposition of a report.</summary>
public enum WishlistReportStatus
{
    /// <summary>The report awaits a decision.</summary>
    Pending,
    /// <summary>An administrator considers the report founded.</summary>
    Upheld,
    /// <summary>An administrator has rejected the report.</summary>
    Dismissed
}
