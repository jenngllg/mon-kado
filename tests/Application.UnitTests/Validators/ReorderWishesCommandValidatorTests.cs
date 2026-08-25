using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class ReorderWishesCommandValidatorTests
{
    private readonly ReorderWishesCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenCommandIsValid_ReturnsSuccess()
    {
        // Arrange
        var command = CreateCommand(
        [
            Guid.CreateVersion7(),
            Guid.CreateVersion7()
        ]);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenWishIdsAreNull_ReturnsMandatoryFailure()
    {
        // Arrange
        var command = CreateCommand(null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            nameof(command.WishIds),
            error.PropertyName);
    }

    [Fact]
    public async Task ValidateAsync_WhenIdentifiersAreInvalid_ReturnsAllFailures()
    {
        // Arrange
        var duplicatedId = Guid.CreateVersion7();
        var command = new ReorderWishesCommand(
            Guid.Empty,
            Guid.Empty,
            [
                Guid.Empty,
                duplicatedId,
                duplicatedId
            ],
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
                nameof(command.WishIds),
                $"{nameof(command.WishIds)}[0]"
            ],
            result.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public async Task ValidateAsync_WhenCollectionExceedsLimit_ReturnsLimitFailure()
    {
        // Arrange
        var wishIds = Enumerable.Range(
                0,
                ReorderWishesCommandValidator.MaximumWishCount + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();
        var command = CreateCommand(wishIds);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            nameof(command.WishIds),
            error.PropertyName);
    }

    private static ReorderWishesCommand CreateCommand(IReadOnlyCollection<Guid>? wishIds)
    {
        return new ReorderWishesCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            wishIds,
            42);
    }
}
