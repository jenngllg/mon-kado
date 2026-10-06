using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class SetWishFavoriteCommandValidatorTests
{
    private readonly SetWishFavoriteCommandValidator _validator = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateAsync_WhenPreferenceIsProvided_AcceptsBothStates(bool preference)
    {
        // Arrange
        var command = new SetWishFavoriteCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            preference,
            1);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenAllValuesAreAbsent_AggregatesRequiredErrors()
    {
        // Arrange
        var command = new SetWishFavoriteCommand(
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            null,
            1);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                "OwnerId",
                "WishlistId",
                "WishId",
                "IsFavorite"
            ],
            result.Errors.Select(error => error.PropertyName));
    }
}
