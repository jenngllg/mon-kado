using JennGllg.Fr.MonKado.Back.Application.Models;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Tracks one export and preserves its cleanup identity after account deletion.</summary>
public class MemberDataExport
{
    /// <summary>Creates a queued request without copying account data.</summary>
    /// <param name="memberId">The requesting member identifier.</param>
    /// <param name="createdAt">The UTC request date.</param>
    public MemberDataExport(
        Guid? memberId,
        DateTime createdAt)
    {
        Id = Guid.CreateVersion7();
        MemberId = memberId;
        CreatedAt = createdAt;
        AvailableAt = createdAt;
    }

    /// <summary>Gets the export identifier.</summary>
    public Guid Id
    {
        get; private set;
    }
    /// <summary>Gets the owner, or null after the account is physically deleted.</summary>
    public Guid? MemberId
    {
        get; private set;
    }
    /// <summary>Gets the persisted lifecycle state.</summary>
    public PersonalDataExportStatus Status { get; private set; } = PersonalDataExportStatus.Queued;
    /// <summary>Gets the UTC request date.</summary>
    public DateTime CreatedAt
    {
        get; private set;
    }
    /// <summary>Gets the earliest next generation attempt date.</summary>
    public DateTime AvailableAt
    {
        get; private set;
    }
    /// <summary>Gets the total number of claimed generation attempts.</summary>
    public int AttemptCount
    {
        get; private set;
    }
    /// <summary>Gets the current fencing token.</summary>
    public Guid? LeaseId
    {
        get; private set;
    }
    /// <summary>Gets the absolute UTC lease deadline.</summary>
    public DateTime? LockedUntil
    {
        get; private set;
    }
    /// <summary>Gets the successful immutable attempt identifier.</summary>
    public Guid? ArchiveId
    {
        get; private set;
    }
    /// <summary>Gets the successful UTC database snapshot date.</summary>
    public DateTime? SnapshotAt
    {
        get; private set;
    }
    /// <summary>Gets the UTC publication date.</summary>
    public DateTime? ReadyAt
    {
        get; private set;
    }
    /// <summary>Gets the absolute UTC archive expiration.</summary>
    public DateTime? ExpiresAt
    {
        get; private set;
    }
    /// <summary>Gets the complete archive length.</summary>
    public long? SizeInBytes
    {
        get; private set;
    }
    /// <summary>Gets the terminal non-sensitive failure classification.</summary>
    public PersonalDataExportFailure? Failure
    {
        get; private set;
    }
    /// <summary>Gets the last confirmed physical cleanup date for a terminal request.</summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework materializes this private setter; cleanup updates the persisted value with ExecuteUpdateAsync.")]
    public DateTime? FilesCleanedAt
    {
        get; private set;
    }

    /// <summary>Claims eligible work, fencing any expired predecessor.</summary>
    /// <param name="now">The current UTC date.</param>
    /// <param name="leaseDuration">The renewable lease duration.</param>
    /// <param name="maximumAttempts">The generation attempt limit.</param>
    /// <returns>Whether a new attempt was claimed.</returns>
    public bool TryClaim(
        DateTime now,
        TimeSpan leaseDuration,
        int maximumAttempts)
    {

        if (MemberId is null || Status is not (PersonalDataExportStatus.Queued or PersonalDataExportStatus.Processing))
            return false;

        if (AvailableAt > now || LockedUntil > now)
            return false;

        if (AttemptCount >= maximumAttempts)
        {
            SetFailed(PersonalDataExportFailure.GenerationFailed);

            return false;
        }

        LeaseId = Guid.CreateVersion7();
        LockedUntil = now.Add(leaseDuration);
        AttemptCount++;
        Status = PersonalDataExportStatus.Processing;

        return true;
    }

    /// <summary>Checks that an acknowledgement still belongs to the live worker.</summary>
    /// <param name="leaseId">The worker fencing token.</param>
    /// <param name="now">The current UTC date.</param>
    /// <returns>Whether the owner and its generation lease remain valid.</returns>
    public bool OwnsLease(
        Guid leaseId,
        DateTime now)
    {

        return MemberId.HasValue && Status is PersonalDataExportStatus.Processing && LeaseId.GetValueOrDefault() == leaseId && LockedUntil.GetValueOrDefault() > now;
    }

    /// <summary>Renews only a live generation lease.</summary>
    /// <param name="leaseId">The worker fencing token.</param>
    /// <param name="now">The current UTC date.</param>
    /// <param name="leaseDuration">The new lease duration.</param>
    /// <returns>Whether renewal succeeded.</returns>
    public bool Renew(
        Guid leaseId,
        DateTime now,
        TimeSpan leaseDuration)
    {

        if (!OwnsLease(
            leaseId,
            now))
            return false;
        LockedUntil = now.Add(leaseDuration);

        return true;
    }

    /// <summary>Publishes the immutable archive without extending any earlier ready archive.</summary>
    /// <param name="leaseId">The successful attempt identifier.</param>
    /// <param name="now">The UTC publication date.</param>
    /// <param name="snapshotAt">The UTC data snapshot date.</param>
    /// <param name="sizeInBytes">The complete archive length.</param>
    /// <param name="lifetime">The absolute download lifetime.</param>
    /// <returns>Whether publication belongs to the still-valid worker.</returns>
    public bool Complete(
        Guid leaseId,
        DateTime now,
        DateTime snapshotAt,
        long sizeInBytes,
        TimeSpan lifetime)
    {

        if (!OwnsLease(
            leaseId,
            now))
            return false;
        ArchiveId = leaseId;
        SnapshotAt = snapshotAt;
        ReadyAt = now;
        ExpiresAt = now.Add(lifetime);
        SizeInBytes = sizeInBytes;
        Status = PersonalDataExportStatus.Ready;
        LeaseId = null;
        LockedUntil = null;

        return true;
    }

    /// <summary>Schedules a clean retry or records a terminal bounded failure.</summary>
    /// <param name="leaseId">The failed attempt identifier.</param>
    /// <param name="now">The UTC failure date.</param>
    /// <param name="retryDelay">The delay before another complete snapshot.</param>
    /// <param name="maximumAttempts">The generation attempt limit.</param>
    /// <param name="failure">The sanitized failure classification.</param>
    /// <returns>Whether the failure belonged to the live attempt.</returns>
    public bool FailAttempt(
        Guid leaseId,
        DateTime now,
        TimeSpan retryDelay,
        int maximumAttempts,
        PersonalDataExportFailure failure)
    {

        if (!OwnsLease(
            leaseId,
            now))
            return false;

        if (failure is PersonalDataExportFailure.TooLarge || AttemptCount >= maximumAttempts)
        {
            SetFailed(failure);

            return true;
        }

        Status = PersonalDataExportStatus.Queued;
        AvailableAt = now.Add(retryDelay);
        LeaseId = null;
        LockedUntil = null;

        return true;
    }

    /// <summary>Releases the active-request slot at the exact download deadline.</summary>
    /// <param name="now">The current UTC date.</param>
    public void Expire(DateTime now)
    {

        if (Status is PersonalDataExportStatus.Ready && ExpiresAt.GetValueOrDefault() <= now)
            Status = PersonalDataExportStatus.Expired;
    }

    /// <summary>Projects public metadata without modifying persisted state during a read.</summary>
    /// <param name="now">The current UTC date.</param>
    /// <returns>The member-visible lifecycle details.</returns>
    public PersonalDataExportDetails GetDetails(DateTime now)
    {
        var status = Status;

        if (status is PersonalDataExportStatus.Ready && ExpiresAt.GetValueOrDefault() <= now)
            status = PersonalDataExportStatus.Expired;

        return new PersonalDataExportDetails
        {
            Id = Id,
            Status = status,
            CreatedAt = CreatedAt,
            SnapshotAt = SnapshotAt,
            ReadyAt = ReadyAt,
            ExpiresAt = ExpiresAt,
            SizeInBytes = SizeInBytes,
            Failure = Failure
        };
    }

    /// <summary>Finishes a failed lifecycle and clears its generation lease.</summary>
    /// <param name="failure">The bounded failure classification.</param>
    private void SetFailed(PersonalDataExportFailure failure)
    {
        Status = PersonalDataExportStatus.Failed;
        Failure = failure;
        LeaseId = null;
        LockedUntil = null;
    }
}
