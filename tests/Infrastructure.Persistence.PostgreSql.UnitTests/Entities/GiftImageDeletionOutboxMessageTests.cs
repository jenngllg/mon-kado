using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Entities;

public class GiftImageDeletionOutboxMessageTests
{
    [Fact]
    public void Create_WhenImageIdentifierIsValid_InitializesAvailableMessage()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        var createdAt = new DateTime(
            2026,
            9,
            5,
            12,
            0,
            0,
            DateTimeKind.Utc);

        // Act
        var message = GiftImageDeletionOutboxMessage.Create(
            imageId,
            createdAt);

        // Assert
        Assert.Equal(
            7,
            message.Id.Version);
        Assert.Equal(
            imageId,
            message.ImageId);
        Assert.Equal(
            createdAt,
            message.CreatedAt);
        Assert.Equal(
            createdAt,
            message.AvailableAt);
        Assert.Equal(
            0,
            message.AttemptCount);
        Assert.Null(message.LockedUntil);
    }

    [Fact]
    public void Create_WhenImageIdentifierIsEmpty_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;

        // Act
        var exception = Record.Exception(() => GiftImageDeletionOutboxMessage.Create(
            Guid.Empty,
            createdAt));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void Claim_WhenCalled_IncrementsAttemptAndSetsLease()
    {
        // Arrange
        var message = GiftImageDeletionOutboxMessage.Create(
            Guid.CreateVersion7(),
            DateTime.UtcNow);
        var lockedUntil = DateTime.UtcNow.AddMinutes(5);

        // Act
        message.Claim(lockedUntil);

        // Assert
        Assert.Equal(
            1,
            message.AttemptCount);
        Assert.Equal(
            lockedUntil,
            message.LockedUntil);
    }

    [Fact]
    public void ScheduleRetry_WhenCalled_SetsAvailabilityAndReleasesLease()
    {
        // Arrange
        var message = GiftImageDeletionOutboxMessage.Create(
            Guid.CreateVersion7(),
            DateTime.UtcNow);
        message.Claim(DateTime.UtcNow.AddMinutes(5));
        var availableAt = DateTime.UtcNow.AddHours(1);

        // Act
        message.ScheduleRetry(availableAt);

        // Assert
        Assert.Equal(
            availableAt,
            message.AvailableAt);
        Assert.Null(message.LockedUntil);
    }
}
