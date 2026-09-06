using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>Records deletion operations at the persistence boundary for HTTP contract tests.</summary>
public class RecordingMemberAccountDeletionService : IMemberAccountDeletionService
{
    public List<Guid> Requests { get; } = [];
    public List<(Guid MemberId, string Token)> Confirmations { get; } = [];

    /// <inheritdoc/>
    public Task RequestAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(memberId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ConfirmAsync(
        Guid memberId,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Confirmations.Add((memberId, token));

        return Task.CompletedTask;
    }
}
