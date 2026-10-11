using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class WishlistSubscriptionTests
{
    [Fact]
    public void Constructor_WhenGivenVerifiedShare_StoresIdentityAndCopiesFingerprint()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var shareId = Guid.CreateVersion7();
        var hash = new byte[32];
        hash[0] = 7;

        // Act
        var subscription = new WishlistSubscription(
            id,
            memberId,
            wishlistId,
            shareId,
            hash);
        hash[0] = 9;

        // Assert
        Assert.Equal(
            id,
            subscription.Id);
        Assert.Equal(
            memberId,
            subscription.MemberId);
        Assert.Equal(
            wishlistId,
            subscription.WishlistId);
        Assert.Equal(
            shareId,
            subscription.ShareLinkId);
        Assert.Equal(
            7,
            subscription.ShareSecretHash[0]);
        Assert.Equal(
            default,
            subscription.CreatedAt);
        Assert.Null(subscription.UpdatedAt);
    }
}
