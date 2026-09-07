using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class WishlistTests
{
    [Fact]
    public void Constructor_WhenValuesAreProvided_InitializesWishlist()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var eventDate = new DateOnly(
            2026,
            9,
            24);

        // Act
        var wishlist = new Wishlist(
            id,
            ownerId,
            "La liste de Léa",
            "LA LISTE DE LÉA",
            WishlistOccasion.Birthday,
            eventDate,
            "Merci d’être là");

        // Assert
        Assert.Equal(
            id,
            wishlist.Id);
        Assert.Equal(
            ownerId,
            wishlist.OwnerId);
        Assert.Equal(
            "La liste de Léa",
            wishlist.Name);
        Assert.Equal(
            "LA LISTE DE LÉA",
            wishlist.NormalizedName);
        Assert.Equal(
            WishlistOccasion.Birthday,
            wishlist.Occasion);
        Assert.Equal(
            eventDate,
            wishlist.EventDate);
        Assert.Equal(
            "Merci d’être là",
            wishlist.Message);
        Assert.Equal(
            default,
            wishlist.CreatedAt);
        Assert.Null(wishlist.UpdatedAt);
        Assert.Equal(
            0u,
            wishlist.Version);
    }

    [Fact]
    public void Update_WhenValuesDiffer_ReplacesEditableMetadata()
    {
        // Arrange
        var wishlist = CreateWishlist();
        var eventDate = new DateOnly(
            2027,
            1,
            2);

        // Act
        var hasChanged = wishlist.Update(
            "Nouvelle liste",
            "NOUVELLE LISTE",
            WishlistOccasion.Wedding,
            eventDate,
            "Nouveau message");

        // Assert
        Assert.True(hasChanged);
        Assert.Equal(
            "Nouvelle liste",
            wishlist.Name);
        Assert.Equal(
            "NOUVELLE LISTE",
            wishlist.NormalizedName);
        Assert.Equal(
            WishlistOccasion.Wedding,
            wishlist.Occasion);
        Assert.Equal(
            eventDate,
            wishlist.EventDate);
        Assert.Equal(
            "Nouveau message",
            wishlist.Message);
    }

    [Fact]
    public void Update_WhenValuesAreIdentical_ReturnsFalse()
    {
        // Arrange
        var wishlist = CreateWishlist();

        // Act
        var hasChanged = wishlist.Update(
            "Liste",
            "LISTE",
            WishlistOccasion.Birthday,
            null,
            null);

        // Assert
        Assert.False(hasChanged);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("normalizedName")]
    [InlineData("occasion")]
    [InlineData("eventDate")]
    [InlineData("message")]
    public void Update_WhenOneValueDiffers_ReturnsTrue(string changedProperty)
    {
        // Arrange
        var wishlist = CreateWishlist();
        var name = "Liste";
        var normalizedName = "LISTE";
        var occasion = WishlistOccasion.Birthday;
        var eventDate = (DateOnly?)null;
        var message = (string?)null;

        switch (changedProperty)
        {
            case "name":
                name = "Nouvelle liste";
                break;
            case "normalizedName":
                normalizedName = "NOUVELLE LISTE";
                break;
            case "occasion":
                occasion = WishlistOccasion.Wedding;
                break;
            case "eventDate":
                eventDate = new DateOnly(
                    2027,
                    1,
                    2);
                break;
            case "message":
                message = "Nouveau message";
                break;
        }

        // Act
        var hasChanged = wishlist.Update(
            name,
            normalizedName,
            occasion,
            eventDate,
            message);

        // Assert
        Assert.True(hasChanged);
    }

    [Fact]
    public void Moderate_WhenSuspending_SetsPrivateReasonAndInitialDate()
    {
        // Arrange
        var wishlist = CreateWishlist();
        var occurredAt = new DateTime(
            2026,
            9,
            6,
            12,
            0,
            0,
            DateTimeKind.Utc);

        // Act
        var changed = wishlist.Moderate(
            true,
            "Reported content",
            occurredAt);

        // Assert
        Assert.True(changed);
        Assert.True(wishlist.IsSuspended);
        Assert.Equal(
            "Reported content",
            wishlist.SuspensionReason);
        Assert.Equal(
            occurredAt,
            wishlist.SuspendedAt);
        Assert.Null(wishlist.UpdatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Moderate_WhenStateIsIdentical_DoesNotChangeDates(bool suspended)
    {
        // Arrange
        var wishlist = CreateWishlist();
        var reason = suspended ? "Reported content" : null;
        wishlist.Moderate(
            suspended,
            reason,
            DateTime.UnixEpoch);

        // Act
        var changed = wishlist.Moderate(
            suspended,
            reason,
            DateTime.UnixEpoch.AddHours(1));

        // Assert
        Assert.False(changed);
        Assert.Equal(
            suspended,
            wishlist.IsSuspended);
        Assert.Equal(
            suspended ? DateTime.UnixEpoch : (DateTime?)null,
            wishlist.SuspendedAt);
    }

    [Fact]
    public void Moderate_WhenAmendingReason_PreservesInitialSuspensionDate()
    {
        // Arrange
        var wishlist = CreateWishlist();
        wishlist.Moderate(
            true,
            "Initial reason",
            DateTime.UnixEpoch);

        // Act
        var changed = wishlist.Moderate(
            true,
            "Corrected reason",
            DateTime.UnixEpoch.AddDays(1));

        // Assert
        Assert.True(changed);
        Assert.True(wishlist.IsSuspended);
        Assert.Equal(
            "Corrected reason",
            wishlist.SuspensionReason);
        Assert.Equal(
            DateTime.UnixEpoch,
            wishlist.SuspendedAt);
    }

    [Fact]
    public void Moderate_WhenReactivating_ClearsPrivateState()
    {
        // Arrange
        var wishlist = CreateWishlist();
        wishlist.Moderate(
            true,
            "Initial reason",
            DateTime.UnixEpoch);

        // Act
        var changed = wishlist.Moderate(
            false,
            null,
            DateTime.UnixEpoch.AddDays(1));

        // Assert
        Assert.True(changed);
        Assert.False(wishlist.IsSuspended);
        Assert.Null(wishlist.SuspensionReason);
        Assert.Null(wishlist.SuspendedAt);
    }

    [Fact]
    public void Moderate_WhenSuspendingAfterReactivation_UsesNewSuspensionDate()
    {
        // Arrange
        var wishlist = CreateWishlist();
        wishlist.Moderate(
            true,
            "Initial reason",
            DateTime.UnixEpoch);
        wishlist.Moderate(
            false,
            null,
            DateTime.UnixEpoch.AddDays(1));
        var newDate = DateTime.UnixEpoch.AddDays(2);

        // Act
        wishlist.Moderate(
            true,
            "Another reason",
            newDate);

        // Assert
        Assert.Equal(
            newDate,
            wishlist.SuspendedAt);
    }

    private static Wishlist CreateWishlist()
    {
        return new Wishlist(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Liste",
            "LISTE",
            WishlistOccasion.Birthday,
            null,
            null);
    }
}
