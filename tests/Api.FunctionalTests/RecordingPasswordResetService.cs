using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingPasswordResetService : IPasswordResetService
{
    private readonly List<string> _requestedEmails = [];
    private readonly List<PasswordResetCall> _resetCalls = [];
    private readonly Lock _sync = new();

    public Exception? Exception
    {
        get;
        set;
    }

    public bool ResetResult
    {
        get;
        set;
    } = true;

    public IReadOnlyList<string> RequestedEmails
    {
        get
        {
            lock (_sync)
            {

                return _requestedEmails.ToArray();
            }
        }
    }

    public IReadOnlyList<PasswordResetCall> ResetCalls
    {
        get
        {
            lock (_sync)
            {

                return _resetCalls.ToArray();
            }
        }
    }

    public Task RequestAsync(
        string email,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Exception is not null)
            throw Exception;

        lock (_sync)
        {
            _requestedEmails.Add(email);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ResetAsync(
        string userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Exception is not null)
            throw Exception;

        lock (_sync)
        {
            _resetCalls.Add(new PasswordResetCall(
                userId,
                token,
                newPassword));
        }

        return Task.FromResult(ResetResult);
    }
}
