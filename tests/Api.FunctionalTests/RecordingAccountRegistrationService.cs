using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingAccountRegistrationService : IAccountRegistrationService
{
    private readonly Lock _sync = new();
    private readonly List<RegistrationCall> _calls = [];
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public IReadOnlyList<RegistrationCall> Calls
    {
        get
        {
            lock (_sync)
            {

                return _calls.ToArray();
            }
        }
    }

    public Task RegisterAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _calls.Add(new RegistrationCall(
                email,
                password,
                displayName));
        }

        Interlocked.Increment(ref _callCount);

        return Task.CompletedTask;
    }
}
