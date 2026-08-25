using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class UpdateWishlistCommandValidatorTests
{
    private readonly UpdateWishlistCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenCommandIsValid_ReturnsSuccess()
    {
        // Arrange
        var command = CreateCommand(
            "Liste 🎁",
            WishlistOccasion.Birthday,
            "Première ligne\nDeuxième ligne");

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
        var command = new UpdateWishlistCommand(
            Guid.Empty,
            Guid.Empty,
            "Liste",
            WishlistOccasion.Other,
            null,
            null,
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.OwnerId));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.WishlistId));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("Liste\ninterdite", 99, "Message\u0001interdit")]
    public async Task ValidateAsync_WhenEditableValuesAreInvalid_ReturnsAllFailures(
        string? name,
        int? occasion,
        string? message)
    {
        // Arrange
        var command = CreateCommand(
            name,
            (WishlistOccasion?)occasion,
            message);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Name));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Occasion));

        if (message is not null)
        {
            Assert.Contains(
                result.Errors,
                error => error.PropertyName == nameof(command.Message));
        }
    }

    private static UpdateWishlistCommand CreateCommand(
        string? name,
        WishlistOccasion? occasion,
        string? message)
    {
        return new UpdateWishlistCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            name,
            occasion,
            null,
            message,
            42);
    }
}
