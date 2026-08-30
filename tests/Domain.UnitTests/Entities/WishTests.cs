using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class WishTests
{
    [Fact]
    public void Update_WhenQuantityChanges_ReplacesQuantity()
    {
        // Arrange
        var wish = CreateWish();

        // Act
        var changed = wish.Update(
            wish.Name,
            wish.Note,
            wish.Url,
            wish.Price,
            4);

        // Assert
        Assert.True(changed);
        Assert.Equal(
            4,
            wish.Quantity);
    }

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

    [Fact]
    public void Update_WhenValuesChange_ReplacesEditableValuesAndReturnsTrue()
    {
        // Arrange
        var wish = CreateWish();

        // Act
        var result = wish.Update(
            "Nouvelle console",
            null,
            "https://example.com/new-console",
            399.99m);

        // Assert
        Assert.True(result);
        Assert.Equal(
            "Nouvelle console",
            wish.Name);
        Assert.Null(wish.Note);
        Assert.Equal(
            "https://example.com/new-console",
            wish.Url);
        Assert.Equal(
            399.99m,
            wish.Price);
        Assert.Equal(
            3,
            wish.Position);
    }

    [Fact]
    public void Update_WhenValuesAreUnchanged_ReturnsFalse()
    {
        // Arrange
        var wish = CreateWish();

        // Act
        var result = wish.Update(
            "Console",
            "Édition blanche",
            "https://example.com/console",
            499.99m);

        // Assert
        Assert.False(result);
        Assert.Equal(
            "Console",
            wish.Name);
    }

    [Fact]
    public void MoveTo_WhenPositionChanges_ReplacesPositionAndReturnsTrue()
    {
        // Arrange
        var wish = CreateWish();

        // Act
        var result = wish.MoveTo(8);

        // Assert
        Assert.True(result);
        Assert.Equal(
            8,
            wish.Position);
    }

    [Fact]
    public void MoveTo_WhenPositionIsUnchanged_ReturnsFalse()
    {
        // Arrange
        var wish = CreateWish();

        // Act
        var result = wish.MoveTo(3);

        // Assert
        Assert.False(result);
        Assert.Equal(
            3,
            wish.Position);
    }

    private static Wish CreateWish()
    {
        return new Wish(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Console",
            "Édition blanche",
            "https://example.com/console",
            499.99m,
            3);
    }
}
