using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Entities;

public class MemberDataExportTests
{
    private static readonly DateTime _now = new(
        2026,
        9,
        7,
        12,
        0,
        0,
        DateTimeKind.Utc);
    private static readonly TimeSpan _leaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _lifetime = TimeSpan.FromHours(24);
    [Fact]
    public void Constructor_WhenCreated_QueuesAnEmptyMemberOwnedRequest()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();

        // Act
        var export = new MemberDataExport(
            memberId,
            _now);

        // Assert
        Assert.Equal(
            7,
            export.Id.Version);
        Assert.Equal(
            memberId,
            export.MemberId);
        Assert.Equal(
            _now,
            export.CreatedAt);
        Assert.Equal(
            _now,
            export.AvailableAt);
        Assert.Equal(
            PersonalDataExportStatus.Queued,
            export.Status);
        Assert.Equal(
            0,
            export.AttemptCount);
        Assert.Null(export.LeaseId);
        Assert.Null(export.LockedUntil);
        Assert.Null(export.ArchiveId);
        Assert.Null(export.SnapshotAt);
        Assert.Null(export.ReadyAt);
        Assert.Null(export.ExpiresAt);
        Assert.Null(export.SizeInBytes);
        Assert.Null(export.Failure);
        Assert.Null(export.FilesCleanedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryClaim_WhenOwnerAbsentOrRequestNotAvailable_DoesNotClaim(bool missingOwner)
    {
        // Arrange
        var export = new MemberDataExport(
            missingOwner ? null : Guid.CreateVersion7(),
            _now.AddMinutes(1));

        // Act
        var claimed = export.TryClaim(
            _now,
            _leaseDuration,
            5);

        // Assert
        Assert.False(claimed);
        Assert.Equal(
            0,
            export.AttemptCount);
        Assert.Null(export.LeaseId);
    }

    [Fact]
    public void TryClaim_WhenEligible_StartsOneFencedAttempt()
    {
        // Arrange
        var export = CreateQueued();

        // Act
        var claimed = export.TryClaim(
            _now,
            _leaseDuration,
            5);

        // Assert
        Assert.True(claimed);
        Assert.Equal(
            PersonalDataExportStatus.Processing,
            export.Status);
        Assert.Equal(
            1,
            export.AttemptCount);
        Assert.Equal(
            7,
            Assert
                .IsType<Guid>(export.LeaseId)
                .Version);
        Assert.Equal(
            _now.Add(_leaseDuration),
            export.LockedUntil);
    }

    [Fact]
    public void TryClaim_WhenLiveWorkerExists_PreservesItsLease()
    {
        // Arrange
        var export = CreateClaimed();
        var leaseId = export.LeaseId;

        // Act
        var claimed = export.TryClaim(
            _now.AddMinutes(1),
            _leaseDuration,
            5);

        // Assert
        Assert.False(claimed);
        Assert.Equal(
            leaseId,
            export.LeaseId);
        Assert.Equal(
            1,
            export.AttemptCount);
    }

    [Fact]
    public void TryClaim_WhenLeaseExpires_FencesThePreviousWorker()
    {
        // Arrange
        var export = CreateClaimed();
        var oldLease = Assert.IsType<Guid>(export.LeaseId);
        var expiration = _now.Add(_leaseDuration);

        // Act
        var claimed = export.TryClaim(
            expiration,
            _leaseDuration,
            5);

        // Assert
        Assert.True(claimed);
        Assert.NotEqual(
            oldLease,
            export.LeaseId);
        Assert.Equal(
            2,
            export.AttemptCount);
        Assert.False(export.OwnsLease(
                oldLease,
                expiration));
        Assert.True(export.OwnsLease(
                Assert.IsType<Guid>(export.LeaseId),
                expiration));
    }

    [Fact]
    public void TryClaim_WhenLastAttemptCrashed_RecordsTerminalFailure()
    {
        // Arrange
        var export = CreateClaimed();

        // Act
        var claimed = export.TryClaim(
            _now.Add(_leaseDuration),
            _leaseDuration,
            1);

        // Assert
        Assert.False(claimed);
        Assert.Equal(
            PersonalDataExportStatus.Failed,
            export.Status);
        Assert.Equal(
            PersonalDataExportFailure.GenerationFailed,
            export.Failure);
        Assert.Null(export.LeaseId);
        Assert.Null(export.LockedUntil);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OwnsLease_WhenNotProcessingOrOwned_ReturnsFalse(int scenario)
    {
        // Arrange
        var export = new MemberDataExport(
            scenario == 0 ? null : Guid.CreateVersion7(),
            _now);

        if (scenario == 2)
            export.TryClaim(
                _now,
                _leaseDuration,
                5);

        // Act
        var owns = export.OwnsLease(
            Guid.CreateVersion7(),
            _now);

        // Assert
        Assert.False(owns);
    }

    [Fact]
    public void Renew_WhenLeaseAlive_ExtendsOnlyTheLeaseDeadline()
    {
        // Arrange
        var export = CreateClaimed();
        var leaseId = Assert.IsType<Guid>(export.LeaseId);

        // Act
        var renewed = export.Renew(
            leaseId,
            _now.AddMinutes(1),
            _leaseDuration);

        // Assert
        Assert.True(renewed);
        Assert.Equal(
            _now.AddMinutes(3),
            export.LockedUntil);
        Assert.Null(export.ExpiresAt);
        Assert.Equal(
            1,
            export.AttemptCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Renew_WhenLeaseExpiredOrMismatched_DoesNotResurrectIt(bool expired)
    {
        // Arrange
        var export = CreateClaimed();
        var lease = expired ? Assert.IsType<Guid>(export.LeaseId) : Guid.CreateVersion7();

        // Act
        var renewed = export.Renew(
            lease,
            _now.Add(_leaseDuration),
            _leaseDuration);

        // Assert
        Assert.False(renewed);
        Assert.Equal(
            _now.Add(_leaseDuration),
            export.LockedUntil);
    }

    [Fact]
    public void Complete_WhenLeaseAlive_PublishesAnAbsoluteImmutableArchive()
    {
        // Arrange
        var export = CreateClaimed();
        var lease = Assert.IsType<Guid>(export.LeaseId);
        var publishedAt = _now.AddMinutes(1);

        // Act
        var completed = export.Complete(
            lease,
            publishedAt,
            _now,
            2048,
            _lifetime);

        // Assert
        Assert.True(completed);
        Assert.Equal(
            PersonalDataExportStatus.Ready,
            export.Status);
        Assert.Equal(
            lease,
            export.ArchiveId);
        Assert.Equal(
            publishedAt,
            export.ReadyAt);
        Assert.Equal(
            _now,
            export.SnapshotAt);
        Assert.Equal(
            publishedAt.Add(_lifetime),
            export.ExpiresAt);
        Assert.Equal(
            2048,
            export.SizeInBytes);
        Assert.Null(export.LeaseId);
        Assert.Null(export.LockedUntil);
    }

    [Fact]
    public void Complete_WhenLeaseExpired_DoesNotPublishAnyArchive()
    {
        // Arrange
        var export = CreateClaimed();

        // Act
        var completed = export.Complete(
            Assert.IsType<Guid>(export.LeaseId),
            _now.Add(_leaseDuration),
            _now,
            2048,
            _lifetime);

        // Assert
        Assert.False(completed);
        Assert.Null(export.ArchiveId);
        Assert.Null(export.ReadyAt);
        Assert.Null(export.ExpiresAt);
    }

    [Fact]
    public void FailAttempt_WhenRetryable_SchedulesAFreshSnapshotWithoutTerminalError()
    {
        // Arrange
        var export = CreateClaimed();

        // Act
        var acknowledged = export.FailAttempt(
            Assert.IsType<Guid>(export.LeaseId),
            _now,
            TimeSpan.FromMinutes(5),
            5,
            PersonalDataExportFailure.GenerationFailed);

        // Assert
        Assert.True(acknowledged);
        Assert.Equal(
            PersonalDataExportStatus.Queued,
            export.Status);
        Assert.Equal(
            _now.AddMinutes(5),
            export.AvailableAt);
        Assert.Null(export.Failure);
        Assert.Null(export.LeaseId);
        Assert.Null(export.LockedUntil);
        Assert.Null(export.SnapshotAt);
    }

    [Theory]
    [InlineData(PersonalDataExportFailure.GenerationFailed)]
    [InlineData(PersonalDataExportFailure.TooLarge)]
    public void FailAttempt_WhenTerminal_PreservesOnlyTheBoundedClassification(PersonalDataExportFailure failure)
    {
        // Arrange
        var export = CreateClaimed();

        // Act
        var acknowledged = export.FailAttempt(
            Assert.IsType<Guid>(export.LeaseId),
            _now,
            TimeSpan.FromMinutes(5),
            1,
            failure);

        // Assert
        Assert.True(acknowledged);
        Assert.Equal(
            PersonalDataExportStatus.Failed,
            export.Status);
        Assert.Equal(
            failure,
            export.Failure);
        Assert.Null(export.LeaseId);
        Assert.Null(export.LockedUntil);
        Assert.False(export.TryClaim(
                _now.AddHours(1),
                _leaseDuration,
                5));
    }

    [Fact]
    public void FailAttempt_WhenLeaseLost_DoesNotChangeTheCurrentWorkerState()
    {
        // Arrange
        var export = CreateClaimed();
        var currentLease = export.LeaseId;

        // Act
        var acknowledged = export.FailAttempt(
            Guid.CreateVersion7(),
            _now,
            TimeSpan.FromMinutes(5),
            5,
            PersonalDataExportFailure.GenerationFailed);

        // Assert
        Assert.False(acknowledged);
        Assert.Equal(
            currentLease,
            export.LeaseId);
        Assert.Equal(
            PersonalDataExportStatus.Processing,
            export.Status);
    }

    [Fact]
    public void GetDetails_WhenArchiveExpires_ProjectsExpirationWithoutMutatingTheEntity()
    {
        // Arrange
        var export = CreateReady();
        var expiresAt = Assert.IsType<DateTime>(export.ExpiresAt);

        // Act
        var before = export.GetDetails(expiresAt.AddTicks(-1));
        var atDeadline = export.GetDetails(expiresAt);

        // Assert
        Assert.Equal(
            PersonalDataExportStatus.Ready,
            before.Status);
        Assert.Equal(
            PersonalDataExportStatus.Expired,
            atDeadline.Status);
        Assert.Equal(
            PersonalDataExportStatus.Ready,
            export.Status);
        Assert.Equal(
            export.Id,
            atDeadline.Id);
        Assert.Equal(
            export.CreatedAt,
            atDeadline.CreatedAt);
        Assert.Equal(
            export.SnapshotAt,
            atDeadline.SnapshotAt);
        Assert.Equal(
            export.ReadyAt,
            atDeadline.ReadyAt);
        Assert.Equal(
            export.ExpiresAt,
            atDeadline.ExpiresAt);
        Assert.Equal(
            export.SizeInBytes,
            atDeadline.SizeInBytes);
        Assert.Null(atDeadline.Failure);
    }

    [Fact]
    public void Expire_WhenDeadlineReached_ReleasesTheActiveSlotWithoutDroppingCleanupIdentity()
    {
        // Arrange
        var export = CreateReady();
        var archiveId = export.ArchiveId;
        var expiresAt = Assert.IsType<DateTime>(export.ExpiresAt);
        export.Expire(expiresAt.AddTicks(-1));
        Assert.Equal(
            PersonalDataExportStatus.Ready,
            export.Status);

        // Act
        export.Expire(expiresAt);
        export.Expire(expiresAt.AddDays(1));

        // Assert
        Assert.Equal(
            PersonalDataExportStatus.Expired,
            export.Status);
        Assert.Equal(
            archiveId,
            export.ArchiveId);
        Assert.Equal(
            expiresAt,
            export.ExpiresAt);
    }

    [Fact]
    public void GetDetails_WhenStillQueued_DoesNotInventArchiveMetadata()
    {
        // Arrange
        var export = CreateQueued();

        // Act
        export.Expire(_now.AddDays(2));
        var details = export.GetDetails(_now);

        // Assert
        Assert.Equal(
            PersonalDataExportStatus.Queued,
            details.Status);
        Assert.Null(details.ExpiresAt);
        Assert.Null(details.ReadyAt);
        Assert.Null(details.SnapshotAt);
        Assert.Null(details.SizeInBytes);
        Assert.Null(details.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnsLease_WhenNotProcessingOrOwnerMissing_RejectsAcknowledgement(bool ownerMissing)
    {
        // Arrange
        var export = new MemberDataExport(
            ownerMissing ? null : Guid.CreateVersion7(),
            _now);

        // Act
        var owns = export.OwnsLease(
            Guid.CreateVersion7(),
            _now);

        // Assert
        Assert.False(owns);
    }

    private static MemberDataExport CreateQueued()
    {

        return new MemberDataExport(
            Guid.CreateVersion7(),
            _now);
    }

    private static MemberDataExport CreateClaimed()
    {
        var export = CreateQueued();
        export.TryClaim(
            _now,
            _leaseDuration,
            5);

        return export;
    }

    private static MemberDataExport CreateReady()
    {
        var export = CreateClaimed();
        export.Complete(
            Assert.IsType<Guid>(export.LeaseId),
            _now,
            _now,
            2048,
            _lifetime);

        return export;
    }
}
