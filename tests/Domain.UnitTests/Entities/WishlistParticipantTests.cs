using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class WishlistParticipantTests
{
    [Fact]
    public void Constructor_WhenGuestJoins_StoresGuestIdentity()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var guestSessionId = Guid.CreateVersion7();

        // Act
        var participant = new WishlistParticipant(
            id,
            wishlistId,
            guestSessionId,
            "Jenn");

        // Assert
        Assert.Equal(
            id,
            participant.Id);
        Assert.Equal(
            wishlistId,
            participant.WishlistId);
        Assert.Equal(
            guestSessionId,
            participant.GuestSessionId);
        Assert.Equal(
            "Jenn",
            participant.GuestDisplayName);
        Assert.Null(participant.MemberId);
        Assert.Equal(
            default,
            participant.CreatedAt);
        Assert.Null(participant.UpdatedAt);
    }

    [Fact]
    public void CreateMember_WhenMemberJoins_StoresMemberIdentity()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();

        // Act
        var participant = WishlistParticipant.CreateMember(
            id,
            wishlistId,
            memberId);

        // Assert
        Assert.Equal(
            id,
            participant.Id);
        Assert.Equal(
            wishlistId,
            participant.WishlistId);
        Assert.Equal(
            memberId,
            participant.MemberId);
        Assert.Null(participant.GuestSessionId);
        Assert.Equal(
            string.Empty,
            participant.GuestDisplayName);
    }

    [Fact]
    public void AttachToMember_WhenGuestExists_ReplacesGuestIdentity()
    {
        // Arrange
        var participant = new WishlistParticipant(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Jenn");
        var memberId = Guid.CreateVersion7();

        // Act
        participant.AttachToMember(memberId);

        // Assert
        Assert.Equal(
            memberId,
            participant.MemberId);
        Assert.Null(participant.GuestSessionId);
        Assert.Equal(
            "Jenn",
            participant.GuestDisplayName);
    }
}
