using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class GiftReservationHistoryTests
{
    private readonly DateTime _createdAt = new(
        2026,
        9,
        5,
        10,
        0,
        0,
        DateTimeKind.Utc);

    [Fact]
    public void Constructor_WhenValuesAreProvided_CreatesActiveLifecycle()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();

        // Act
        var history = new GiftReservationHistory(
            id,
            memberId,
            wishlistId,
            "Birthday",
            wishId,
            "Book",
            2,
            _createdAt,
            _createdAt);

        // Assert
        Assert.Equal(
            id,
            history.Id);
        Assert.Equal(
            memberId,
            history.MemberId);
        Assert.Equal(
            wishlistId,
            history.WishlistId);
        Assert.Equal(
            "Birthday",
            history.WishlistName);
        Assert.Equal(
            wishId,
            history.WishId);
        Assert.Equal(
            "Book",
            history.WishName);
        Assert.Equal(
            2,
            history.Quantity);
        Assert.Equal(
            GiftReservationHistoryStatus.Active,
            history.Status);
        Assert.Equal(
            _createdAt,
            history.CreatedAt);
        Assert.Equal(
            _createdAt,
            history.LastActivityAt);
        Assert.Null(history.EndedAt);
    }

    [Fact]
    public void UpdateQuantity_WhenQuantityChanges_UpdatesQuantityAndActivity()
    {
        // Arrange
        var history = CreateHistory();
        var activityAt = _createdAt.AddHours(1);

        // Act
        var result = history.UpdateQuantity(
            3,
            activityAt);

        // Assert
        Assert.True(result);
        Assert.Equal(
            3,
            history.Quantity);
        Assert.Equal(
            activityAt,
            history.LastActivityAt);
    }

    [Fact]
    public void UpdateQuantity_WhenQuantityIsUnchanged_KeepsActivity()
    {
        // Arrange
        var history = CreateHistory();

        // Act
        var result = history.UpdateQuantity(
            2,
            _createdAt.AddHours(1));

        // Assert
        Assert.False(result);
        Assert.Equal(
            _createdAt,
            history.LastActivityAt);
    }

    [Theory]
    [InlineData(GiftReservationHistoryStatus.Cancelled)]
    [InlineData(GiftReservationHistoryStatus.Unavailable)]
    public void End_WhenStatusIsTerminal_EndsLifecycle(GiftReservationHistoryStatus status)
    {
        // Arrange
        var history = CreateHistory();
        var endedAt = _createdAt.AddHours(1);

        // Act
        var result = history.End(
            status,
            endedAt);

        // Assert
        Assert.True(result);
        Assert.Equal(
            status,
            history.Status);
        Assert.Equal(
            endedAt,
            history.LastActivityAt);
        Assert.Equal(
            endedAt,
            history.EndedAt);
    }

    [Fact]
    public void End_WhenLifecycleAlreadyEnded_KeepsOriginalOutcome()
    {
        // Arrange
        var history = CreateHistory();
        var firstEndedAt = _createdAt.AddHours(1);
        history.End(
            GiftReservationHistoryStatus.Cancelled,
            firstEndedAt);

        // Act
        var result = history.End(
            GiftReservationHistoryStatus.Unavailable,
            _createdAt.AddHours(2));

        // Assert
        Assert.False(result);
        Assert.Equal(
            GiftReservationHistoryStatus.Cancelled,
            history.Status);
        Assert.Equal(
            firstEndedAt,
            history.EndedAt);
    }

    [Fact]
    public void End_WhenStatusIsActive_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var history = CreateHistory();

        // Act
        var action = () =>
        {
            history.End(
                GiftReservationHistoryStatus.Active,
                _createdAt.AddHours(1));
        };

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    private GiftReservationHistory CreateHistory()
    {
        return new GiftReservationHistory(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Birthday",
            Guid.CreateVersion7(),
            "Book",
            2,
            _createdAt,
            _createdAt);
    }
}
