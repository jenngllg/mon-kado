using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class DeleteWishCommandValidatorTests
{
    private readonly DeleteWishCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenCommandIsValid_ReturnsSuccess()
    {
        // Arrange
        var command = new DeleteWishCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenIdentifiersAreEmpty_ReturnsIdentifierFailures()
    {
        // Arrange
        var command = new DeleteWishCommand(
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                nameof(command.OwnerId),
                nameof(command.WishlistId),
                nameof(command.WishId)
            ],
            result.Errors.Select(error => error.PropertyName));
    }
}
