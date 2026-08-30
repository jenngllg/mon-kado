using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class GiftReservationTests
{
    [Fact]
    public void Constructor_WhenValuesAreProvided_ExposesReservationState()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var participantId = Guid.CreateVersion7();

        // Act
        var reservation = new GiftReservation(
            id,
            wishlistId,
            wishId,
            participantId,
            2);

        // Assert
        Assert.Equal(
            id,
            reservation.Id);
        Assert.Equal(
            wishlistId,
            reservation.WishlistId);
        Assert.Equal(
            wishId,
            reservation.WishId);
        Assert.Equal(
            participantId,
            reservation.WishlistParticipantId);
        Assert.Equal(
            2,
            reservation.Quantity);
        Assert.Equal(
            default,
            reservation.CreatedAt);
        Assert.Null(reservation.UpdatedAt);
        Assert.Equal(
            0u,
            reservation.Version);
    }

    [Fact]
    public void UpdateQuantity_WhenQuantityChanges_ReplacesQuantity()
    {
        // Arrange
        var reservation = CreateReservation();

        // Act
        var changed = reservation.UpdateQuantity(3);

        // Assert
        Assert.True(changed);
        Assert.Equal(
            3,
            reservation.Quantity);
    }

    [Fact]
    public void UpdateQuantity_WhenQuantityIsUnchanged_ReturnsFalse()
    {
        // Arrange
        var reservation = CreateReservation();

        // Act
        var changed = reservation.UpdateQuantity(2);

        // Assert
        Assert.False(changed);
        Assert.Equal(
            2,
            reservation.Quantity);
    }

    [Fact]
    public void TransferTo_WhenParticipantChanges_ReplacesParticipant()
    {
        // Arrange
        var reservation = CreateReservation();
        var participantId = Guid.CreateVersion7();

        // Act
        reservation.TransferTo(participantId);

        // Assert
        Assert.Equal(
            participantId,
            reservation.WishlistParticipantId);
    }

    private static GiftReservation CreateReservation()
    {
        return new GiftReservation(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            2);
    }
}
