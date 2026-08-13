using JennGllg.Fr.MonKado.Back.Application.Accounts;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests;

public sealed class RegisterAccountCommandHandlerTests
{
    [Fact]
    public async Task HandlerTrimsProfileFieldsButPreservesPasswordExactly()
    {
        RecordingAccountRegistrationService registrationService = new();
        RegisterAccountCommandHandler handler = new(registrationService);
        const string Password = "  a secure password  ";

        await handler.Handle(
            new RegisterAccountCommand(" Lea@example.fr ", Password, " Léa "),
            TestContext.Current.CancellationToken);

        Assert.Equal("Lea@example.fr", registrationService.Email);
        Assert.Equal(Password, registrationService.Password);
        Assert.Equal("Léa", registrationService.DisplayName);
    }

    private sealed class RecordingAccountRegistrationService : IAccountRegistrationService
    {
        public string? Email { get; private set; }

        public string? Password { get; private set; }

        public string? DisplayName { get; private set; }

        public Task RegisterAsync(
            string email,
            string password,
            string displayName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Email = email;
            Password = password;
            DisplayName = displayName;
            return Task.CompletedTask;
        }
    }
}
