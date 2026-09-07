using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>Retains an immutable private review throughout the lifetime of a report.</summary>
public class WishlistReportReviewEvent
{
    private WishlistReportReviewEvent()
    {
    }

    /// <summary>Initializes a durable review event.</summary>
    /// <param name="id">The application-generated event identifier.</param>
    /// <param name="report">The report after an effective review, copied without retaining a mutable reference.</param>
    /// <param name="sequence">The strictly increasing report review sequence.</param>
    /// <param name="previousStatus">The previous disposition.</param>
    /// <exception cref="InvalidOperationException">The report has not been reviewed.</exception>
    public WishlistReportReviewEvent(
        Guid id,
        WishlistReport report,
        long sequence,
        WishlistReportStatus previousStatus)
    {
        Id = id;
        ReportId = report.Id;
        Sequence = sequence;
        PreviousStatus = previousStatus;
        Status = report.Status;
        Note = report.ReviewNote;
        AdministratorId = report.ReviewedByAdministratorId;
        OccurredAt = report.ReviewedAt ?? throw new InvalidOperationException("A review event requires a reviewed report.");
    }

    /// <summary>Gets the event identifier.</summary>
    public Guid Id
    {
        get; private set;
    }
    /// <summary>Gets the report identifier.</summary>
    public Guid ReportId
    {
        get; private set;
    }
    /// <summary>Gets the durable order within the report.</summary>
    public long Sequence
    {
        get; private set;
    }
    /// <summary>Gets the previous disposition.</summary>
    public WishlistReportStatus PreviousStatus
    {
        get; private set;
    }
    /// <summary>Gets the new disposition.</summary>
    public WishlistReportStatus Status
    {
        get; private set;
    }
    /// <summary>Gets the private review note.</summary>
    public string? Note
    {
        get; private set;
    }
    /// <summary>Gets the deciding administrator, or null after account deletion.</summary>
    public Guid? AdministratorId
    {
        get; private set;
    }
    /// <summary>Gets the UTC review date.</summary>
    public DateTime OccurredAt
    {
        get; private set;
    }
}
