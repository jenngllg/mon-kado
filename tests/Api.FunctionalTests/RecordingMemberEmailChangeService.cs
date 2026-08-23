using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingMemberEmailChangeService : IMemberEmailChangeService
{
    public List<(Guid MemberId, string Email, string CurrentPassword, uint ExpectedVersion)> Requests
    {
        get;
    } = [];

    public List<(Guid RequestId, string Token)> Confirmations { get; } = [];

    public bool RequestResult { get; set; } = true;

    public bool ConfirmationResult { get; set; } = true;

    public Exception? RequestException
    {
        get; set;
    }

    public Exception? ConfirmationException
    {
        get; set;
    }

    public Task<bool> RequestAsync(
        Guid memberId,
        string email,
        string currentPassword,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add((
            memberId,
            email,
            currentPassword,
            expectedVersion));

        if (RequestException is not null)
            throw RequestException;

        return Task.FromResult(RequestResult);
    }

    public Task<bool> ConfirmAsync(
        Guid requestId,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Confirmations.Add((
            requestId,
            token));

        if (ConfirmationException is not null)
            throw ConfirmationException;

        return Task.FromResult(ConfirmationResult);
    }
}
