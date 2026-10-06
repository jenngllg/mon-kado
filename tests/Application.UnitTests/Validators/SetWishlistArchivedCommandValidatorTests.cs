using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class SetWishlistArchivedCommandValidatorTests
{
    private readonly SetWishlistArchivedCommandValidator _validator = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenStateIsPresent_AcceptsBothStates(bool isArchived)
    {
        // Arrange
        var command = new SetWishlistArchivedCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            isArchived,
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenIdentifiersAndStateAreMissing_AggregatesAllFailures()
    {
        // Arrange
        var command = new SetWishlistArchivedCommand(
            Guid.Empty,
            Guid.Empty,
            null,
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
                nameof(command.IsArchived)
            ],
            result.Errors.Select(error => error.PropertyName));
    }
}
