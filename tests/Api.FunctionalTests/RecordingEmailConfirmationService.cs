using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingEmailConfirmationService : IEmailConfirmationService
{
    private readonly Lock _sync = new();
    private readonly List<EmailConfirmationCall> _confirmationCalls = [];
    private readonly List<string> _requestedEmails = [];
    private int _confirmCallCount;
    private int _requestCallCount;

    public bool ConfirmationResult { get; set; } = true;

    public int ConfirmCallCount => Volatile.Read(ref _confirmCallCount);

    public int RequestCallCount => Volatile.Read(ref _requestCallCount);

    public IReadOnlyList<EmailConfirmationCall> ConfirmationCalls
    {
        get
        {
            lock (_sync)
            {

                return _confirmationCalls.ToArray();
            }
        }
    }

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

    public Task<bool> ConfirmAsync(
        string userId,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _confirmationCalls.Add(new EmailConfirmationCall(
                userId,
                token));
        }

        Interlocked.Increment(ref _confirmCallCount);

        return Task.FromResult(ConfirmationResult);
    }

    public Task RequestAsync(
        string email,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _requestedEmails.Add(email);
        }

        Interlocked.Increment(ref _requestCallCount);

        return Task.CompletedTask;
    }
}
