using JennGllg.Fr.MonKado.Back.Application.Commands;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CreateWishImportPreviewCommandTests
{
    [Fact]
    public void Constructor_WhenValuesAreProvided_PreservesRequestValues()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        const string url = "https://example.com";

        // Act
        var command = new CreateWishImportPreviewCommand(
            ownerId,
            wishlistId,
            url);

        // Assert
        Assert.Equal(
            ownerId,
            command.OwnerId);
        Assert.Equal(
            wishlistId,
            command.WishlistId);
        Assert.Equal(
            url,
            command.Url);
    }
}
