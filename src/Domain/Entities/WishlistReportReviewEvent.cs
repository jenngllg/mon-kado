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
    /// <param name="reportId">The report identifier.</param>
    /// <param name="sequence">The strictly increasing report review sequence.</param>
    /// <param name="previousStatus">The previous disposition.</param>
    /// <param name="status">The new disposition.</param>
    /// <param name="note">The normalized private review note.</param>
    /// <param name="administratorId">The deciding administrator.</param>
    /// <param name="occurredAt">The UTC review date.</param>
    public WishlistReportReviewEvent(
        Guid id,
        Guid reportId,
        long sequence,
        WishlistReportStatus previousStatus,
        WishlistReportStatus status,
        string? note,
        Guid administratorId,
        DateTime occurredAt)
    {
        Id = id;
        ReportId = reportId;
        Sequence = sequence;
        PreviousStatus = previousStatus;
        Status = status;
        Note = note;
        AdministratorId = administratorId;
        OccurredAt = occurredAt;
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
