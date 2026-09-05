using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class WishTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemoveImage_WhenCalled_ClearsReferenceAndHash(
        bool hasImage)
    {
        // Arrange
        var wish = CreateWish();
        var imageId = Guid.CreateVersion7();

        if (hasImage)
            wish.ReplaceImage(
                imageId,
                new byte[Wish.ImageContentHashLength]);

        // Act
        var removedImageId = wish.RemoveImage();

        // Assert
        Assert.Equal(
            hasImage ? imageId : (Guid?)null,
            removedImageId);
        Assert.Null(wish.ImageId);
        Assert.Null(wish.ImageContentHash);
    }

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

    [Fact]
    public void ReplaceImage_WhenImageIsNew_AttachesDefensiveHashCopy()
    {
        // Arrange
        var wish = CreateWish();
        var imageId = Guid.CreateVersion7();
        var contentHash = Enumerable
            .Range(
                0,
                Wish.ImageContentHashLength)
            .Select(value => (byte)value)
            .ToArray();

        // Act
        var replacedImageId = wish.ReplaceImage(
            imageId,
            contentHash);
        contentHash[0] = byte.MaxValue;
        var exposedHash = wish.ImageContentHash;
        exposedHash?[1] = byte.MaxValue;

        // Assert
        Assert.Null(replacedImageId);
        Assert.Equal(
            imageId,
            wish.ImageId);
        Assert.NotNull(wish.ImageContentHash);
        Assert.Equal(
            byte.MinValue,
            wish.ImageContentHash[0]);
        Assert.Equal(
            1,
            wish.ImageContentHash[1]);
    }

    [Fact]
    public void ReplaceImage_WhenImageExists_ReturnsReplacedIdentifier()
    {
        // Arrange
        var wish = CreateWish();
        var oldImageId = Guid.CreateVersion7();
        var newImageId = Guid.CreateVersion7();
        wish.ReplaceImage(
            oldImageId,
            new byte[Wish.ImageContentHashLength]);

        // Act
        var replacedImageId = wish.ReplaceImage(
            newImageId,
            Enumerable.Repeat(
                (byte)1,
                Wish.ImageContentHashLength).ToArray());

        // Assert
        Assert.Equal(
            oldImageId,
            replacedImageId);
        Assert.Equal(
            newImageId,
            wish.ImageId);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void HasImageContentHash_WhenHashStateVaries_ReturnsExpectedResult(
        bool useMatchingHash,
        bool useInvalidLength)
    {
        // Arrange
        var wish = CreateWish();
        var persistedHash = Enumerable.Repeat(
            (byte)1,
            Wish.ImageContentHashLength).ToArray();
        wish.ReplaceImage(
            Guid.CreateVersion7(),
            persistedHash);
        var comparedHash = useInvalidLength
            ? new byte[1]
            : Enumerable.Repeat(
                useMatchingHash
                    ? (byte)1
                    : (byte)2,
                Wish.ImageContentHashLength).ToArray();

        // Act
        var result = wish.HasImageContentHash(comparedHash);

        // Assert
        Assert.Equal(
            useMatchingHash,
            result);
    }

    [Fact]
    public void HasImageContentHash_WhenWishHasNoImage_ReturnsFalse()
    {
        // Arrange
        var wish = CreateWish();

        // Act
        var result = wish.HasImageContentHash(new byte[Wish.ImageContentHashLength]);

        // Assert
        Assert.False(result);
        Assert.Null(wish.ImageContentHash);
    }

    [Theory]
    [InlineData(true, false, typeof(ArgumentOutOfRangeException))]
    [InlineData(false, true, typeof(ArgumentException))]
    public void ReplaceImage_WhenArgumentsAreInvalid_ThrowsExpectedException(
        bool imageIdIsEmpty,
        bool hashLengthIsInvalid,
        Type expectedExceptionType)
    {
        // Arrange
        var wish = CreateWish();
        var imageId = imageIdIsEmpty
            ? Guid.Empty
            : Guid.CreateVersion7();
        var contentHash = new byte[hashLengthIsInvalid
            ? 1
            : Wish.ImageContentHashLength];

        // Act
        var exception = Record.Exception(() => wish.ReplaceImage(
            imageId,
            contentHash));

        // Assert
        Assert.IsType(
            expectedExceptionType,
            exception);
    }

    [Fact]
    public void ReplaceImage_WhenContentHashIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var wish = CreateWish();

        // Act
        var exception = Record.Exception(() => wish.ReplaceImage(
            Guid.CreateVersion7(),
            null!));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
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
