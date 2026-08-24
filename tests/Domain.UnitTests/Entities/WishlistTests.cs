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
}
