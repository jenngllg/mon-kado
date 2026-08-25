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
