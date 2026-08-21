using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class RegisterAccountCommandValidatorTests
{
    private readonly RegisterAccountCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenRegistrationIsValid_ReturnsSuccess()
    {
        // Arrange
        var command = new RegisterAccountCommand(
            " Lea@example.fr ",
            " a secure password ",
            " Léa ");

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
    [InlineData("not-an-email")]
    [InlineData("person@example.fr extra")]
    [InlineData("Léa <lea@example.fr>")]
    public async Task ValidateAsync_WhenEmailIsInvalid_ReturnsEmailFailure(string? email)
    {
        // Arrange
        var command = new RegisterAccountCommand(
            email,
            "a secure password",
            "Léa");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Email));
    }

    [Fact]
    public async Task ValidateAsync_WhenEmailExceedsMaximumLength_ReturnsEmailFailure()
    {
        // Arrange
        var email = new string(
            'a',
            244) + "@example.fr";
        var command = new RegisterAccountCommand(
            email,
            "a secure password",
            "Léa");

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
    [InlineData("short value")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ValidateAsync_WhenPasswordIsTooShort_ReturnsPasswordFailure(
        string? password)
    {
        // Arrange
        var command = new RegisterAccountCommand(
            "lea@example.fr",
            password,
            "Léa");

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
        var command = new RegisterAccountCommand(
            "lea@example.fr",
            new string(
                'a',
                129),
            "Léa");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Léa\nMartin")]
    public async Task ValidateAsync_WhenDisplayNameIsInvalid_ReturnsDisplayNameFailure(
        string? displayName)
    {
        // Arrange
        var command = new RegisterAccountCommand(
            "lea@example.fr",
            "a secure password",
            displayName);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.DisplayName));
    }

    [Fact]
    public async Task ValidateAsync_WhenDisplayNameExceedsMaximumLength_ReturnsDisplayNameFailure()
    {
        // Arrange
        var command = new RegisterAccountCommand(
            "lea@example.fr",
            "a secure password",
            new string(
                'a',
                81));

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.DisplayName));
    }
}
