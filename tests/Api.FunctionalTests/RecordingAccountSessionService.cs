using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingAccountSessionService : IAccountSessionService
{
    private readonly List<LoginCall> _calls = [];
    private readonly Lock _sync = new();
    private int _refreshCallCount;

    public AccountSessionTokens Tokens { get; set; } = CreateTokens();

    public AccountLoginResult Result { get; set; } = AccountLoginResult.Success;

    public bool RefreshSucceeds { get; set; } = true;

    public int RefreshCallCount
    {
        get
        {
            lock (_sync)
            {

                return _refreshCallCount;
            }
        }
    }

    public IReadOnlyList<LoginCall> Calls
    {
        get
        {
            lock (_sync)
            {

                return _calls.ToArray();
            }
        }
    }

    public Task<AccountSessionLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _calls.Add(new LoginCall(
                email,
                password,
                rememberMe,
                currentRefreshToken));
        }

        var tokens = Result == AccountLoginResult.Success
            ? new AccountSessionTokens(
                Tokens.AccessToken,
                Tokens.RefreshToken,
                Tokens.RefreshTokenExpiresAt,
                rememberMe)
            : null;

        return Task.FromResult(new AccountSessionLoginResult(
            Result,
            tokens));
    }

    public Task<AccountSessionTokens?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _refreshCallCount++;
        }

        return Task.FromResult(
            RefreshSucceeds
                ? Tokens
                : null);
    }

    private static AccountSessionTokens CreateTokens()
    {
        return new AccountSessionTokens(
            new AccessToken(
                "functional-access-token",
                900),
            "functional-refresh-token",
            new DateTime(
                2030,
                1,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc),
            false);
    }
}
