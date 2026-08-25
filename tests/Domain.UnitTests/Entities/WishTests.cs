using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class WishTests
{
    [Fact]
    public void Constructor_WhenValuesAreProvided_InitializesWish()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();

        // Act
        var wish = new Wish(
            id,
            wishlistId,
            "Console",
            "Édition blanche",
            "https://example.com/console",
            499.99m,
            3);

        // Assert
        Assert.Equal(
            id,
            wish.Id);
        Assert.Equal(
            wishlistId,
            wish.WishlistId);
        Assert.Equal(
            "Console",
            wish.Name);
        Assert.Equal(
            "Édition blanche",
            wish.Note);
        Assert.Equal(
            "https://example.com/console",
            wish.Url);
        Assert.Equal(
            499.99m,
            wish.Price);
        Assert.Equal(
            3,
            wish.Position);
        Assert.Equal(
            default,
            wish.CreatedAt);
        Assert.Null(wish.UpdatedAt);
        Assert.Equal(
            0u,
            wish.Version);
    }
}
