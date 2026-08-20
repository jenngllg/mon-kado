using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests;

public sealed class LoginCommandHandlerTests
{
    [Fact]
    public async Task HandlerTrimsEmailButPreservesPasswordAndRememberMe()
    {
        RecordingSessionService service = new(AccountLoginResult.Success);
        LoginCommandHandler handler = new(service);
        const string Password = "  exact password  ";

        await handler.Handle(
            new LoginCommand(" Lea@example.fr ", Password, RememberMe: true),
            TestContext.Current.CancellationToken);

        Assert.Equal("Lea@example.fr", service.Email);
        Assert.Equal(Password, service.Password);
        Assert.True(service.RememberMe);
    }

    [Theory]
    [InlineData(AccountLoginResult.InvalidCredentials, typeof(InvalidCredentialsException))]
    [InlineData(AccountLoginResult.EmailNotConfirmed, typeof(EmailNotConfirmedException))]
    public async Task HandlerMapsFailedResultsToPublicExceptions(
        AccountLoginResult result,
        Type expectedException)
    {
        LoginCommandHandler handler = new(new RecordingSessionService(result));

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            handler.Handle(
                new LoginCommand("lea@example.fr", "password"),
                TestContext.Current.CancellationToken));

        Assert.IsType(expectedException, exception);
    }

    private sealed class RecordingSessionService(AccountLoginResult result) : IAccountSessionService
    {
        public string? Email { get; private set; }

        public string? Password { get; private set; }

        public bool RememberMe { get; private set; }

        public Task<AccountLoginResult> LoginAsync(
            string email,
            string password,
            bool rememberMe,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Email = email;
            Password = password;
            RememberMe = rememberMe;
            return Task.FromResult(result);
        }
    }
}
