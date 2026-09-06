using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class UpsertWishImageCommandValidatorTests
{
    private readonly UpsertWishImageCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenCommandIsComplete_ReturnsSuccess()
    {
        // Arrange
        var command = CreateCommand(
            [1],
            true);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(true, false, false, nameof(UpsertWishImageCommand.OwnerId))]
    [InlineData(false, true, false, nameof(UpsertWishImageCommand.WishlistId))]
    [InlineData(false, false, true, nameof(UpsertWishImageCommand.WishId))]
    public async Task ValidateAsync_WhenIdentifierIsEmpty_ReturnsIdentifierFailure(
        bool ownerIsEmpty,
        bool wishlistIsEmpty,
        bool wishIsEmpty,
        string propertyName)
    {
        // Arrange
        var command = new UpsertWishImageCommand(
            ownerIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishlistIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            [1],
            42,
            true);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == propertyName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ValidateAsync_WhenMultipartImageIsInvalid_ReturnsImageFailure(
        bool isEmpty,
        bool hasInvalidShape)
    {
        // Arrange
        var image = isEmpty
            ? []
            : new byte[GiftImageConstraints.MaximumInputLength + 1];
        var command = CreateCommand(
            image,
            !hasInvalidShape);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Image));
    }

    private static UpsertWishImageCommand CreateCommand(
        byte[] image,
        bool hasValidMultipartShape)
    {
        return new UpsertWishImageCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            image,
            42,
            hasValidMultipartShape);
    }
}
