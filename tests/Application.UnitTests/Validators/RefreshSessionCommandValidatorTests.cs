using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class RefreshSessionCommandValidatorTests
{
    private readonly RefreshSessionCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenRefreshTokenIsPresent_ReturnsValidResult()
    {
        // Arrange
        var command = new RefreshSessionCommand("refresh-token");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ValidateAsync_WhenRefreshTokenIsMissing_ReturnsRefreshTokenFailure(
        string? refreshToken)
    {
        // Arrange
        var command = new RefreshSessionCommand(refreshToken);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.RefreshToken));
    }
}
