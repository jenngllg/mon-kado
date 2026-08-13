using JennGllg.Fr.MonKado.Back.Application.Accounts;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests;

public sealed class RegisterAccountCommandValidatorTests
{
    private readonly RegisterAccountCommandValidator validator = new();

    [Fact]
    public async Task ValidRegistrationPassesValidation()
    {
        RegisterAccountCommand command = new(
            " Lea@example.fr ",
            " a secure password ",
            " Léa ");

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("person@example.fr extra")]
    public async Task InvalidEmailFailsValidation(string? email)
    {
        RegisterAccountCommand command = new(email, "a secure password", "Léa");

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Email));
    }

    [Fact]
    public async Task EmailLongerThan254UnicodeScalarsFailsValidation()
    {
        string email = new string('a', 244) + "@example.fr";
        RegisterAccountCommand command = new(email, "a secure password", "Léa");

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Email));
    }

    [Theory]
    [InlineData("short value")]
    [InlineData("")]
    [InlineData(null)]
    public async Task PasswordShorterThan12UnicodeScalarsFailsValidation(string? password)
    {
        RegisterAccountCommand command = new("lea@example.fr", password, "Léa");

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Password));
    }

    [Fact]
    public async Task PasswordLongerThan128UnicodeScalarsFailsValidation()
    {
        RegisterAccountCommand command = new("lea@example.fr", new string('a', 129), "Léa");

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Léa\nMartin")]
    public async Task InvalidDisplayNameFailsValidation(string? displayName)
    {
        RegisterAccountCommand command = new("lea@example.fr", "a secure password", displayName);

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.DisplayName));
    }

    [Fact]
    public async Task DisplayNameLongerThan80UnicodeScalarsFailsValidation()
    {
        RegisterAccountCommand command = new("lea@example.fr", "a secure password", new string('a', 81));

        FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.DisplayName));
    }
}
