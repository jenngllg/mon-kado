using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenLoginIsValid_AcceptsLegacyPassword()
    {
        // Arrange
        var command = new LoginCommand(
            " Lea@example.fr ",
            "legacy");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        Assert.False(command.RememberMe);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task ValidateAsync_WhenEmailIsInvalid_ReturnsEmailFailure(string? email)
    {
        // Arrange
        var command = new LoginCommand(
            email,
            "password");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_WhenPasswordIsMissing_ReturnsPasswordFailure(
        string? password)
    {
        // Arrange
        var command = new LoginCommand(
            "lea@example.fr",
            password);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Password));
    }

    [Fact]
    public async Task ValidateAsync_WhenPasswordExceedsMaximumLength_ReturnsPasswordFailure()
    {
        // Arrange
        var command = new LoginCommand(
            "lea@example.fr",
            new string(
                'a',
                129));

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Password));
    }
}
