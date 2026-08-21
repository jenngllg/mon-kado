using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingAccountSessionService : IAccountSessionService
{
    private readonly Lock _sync = new();
    private readonly List<LoginCall> _calls = [];

    public AccountLoginResult Result { get; set; } = AccountLoginResult.Success;

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

    public Task<AccountLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _calls.Add(new LoginCall(
                email,
                password,
                rememberMe));
        }

        return Task.FromResult(Result);
    }
}
