using JennGllg.Fr.MonKado.Back.Application.Accounts;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests;

public sealed class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator validator = new();

    [Fact]
    public async Task ValidLoginAcceptsShortLegacyPasswordAndDefaultRememberMe()
    {
        LoginCommand command = new(" Lea@example.fr ", "legacy");

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.False(command.RememberMe);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task InvalidEmailFailsValidation(string? email)
    {
        LoginCommand command = new(email, "password");

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingPasswordFailsValidation(string? password)
    {
        LoginCommand command = new("lea@example.fr", password);

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Password));
    }

    [Fact]
    public async Task PasswordLongerThan128UnicodeScalarsFailsValidation()
    {
        LoginCommand command = new("lea@example.fr", new string('a', 129));

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Password));
    }
}
