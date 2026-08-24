using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>
/// Pauses one committed transaction before simulating a lost acknowledgement.
/// </summary>
public class CoordinatedAmbiguousCommitInterceptor : DbTransactionInterceptor
{
    private readonly TaskCompletionSource _firstTransactionCommitted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseFailure = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _throwAfterNextCommit;

    /// <summary>
    /// Arms the interceptor for the next committed transaction.
    /// </summary>
    public void Arm()
    {
        Interlocked.Exchange(
            ref _throwAfterNextCommit,
            1);
    }

    /// <summary>
    /// Waits until the first transaction has committed in PostgreSQL.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous wait.</returns>
    public async Task WaitForFirstCommitAsync(CancellationToken cancellationToken)
    {
        await _firstTransactionCommitted.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Allows the first caller to observe its simulated lost acknowledgement.
    /// </summary>
    public void ReleaseFailure()
    {
        _releaseFailure.TrySetResult();
    }

    /// <inheritdoc />
    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken)
    {

        if (Interlocked.Exchange(
            ref _throwAfterNextCommit,
            0) != 1)
            return;

        _firstTransactionCommitted.TrySetResult();
        await _releaseFailure.Task.WaitAsync(cancellationToken);

        throw new TimeoutException(
            "The commit acknowledgement was lost after a concurrent request completed.");
    }
}
