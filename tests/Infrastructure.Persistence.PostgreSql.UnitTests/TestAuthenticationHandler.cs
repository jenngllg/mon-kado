using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class TestAuthenticationHandler : IAuthenticationHandler
{
    public int InitializationCount
    {
        get; private set;
    }

    public Task<AuthenticateResult> AuthenticateAsync()
    {

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    public Task ChallengeAsync(AuthenticationProperties? properties)
    {

        return Task.CompletedTask;
    }

    public Task ForbidAsync(AuthenticationProperties? properties)
    {

        return Task.CompletedTask;
    }

    public Task InitializeAsync(
        AuthenticationScheme scheme,
        HttpContext context)
    {
        InitializationCount++;

        return Task.CompletedTask;
    }
}
