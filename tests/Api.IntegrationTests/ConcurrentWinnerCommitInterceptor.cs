using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>
/// Rolls back one commit attempt and pauses its verification until a concurrent winner commits.
/// </summary>
public class ConcurrentWinnerCommitInterceptor : DbTransactionInterceptor, IDbCommandInterceptor
{
    private readonly TaskCompletionSource _firstCommitAttempted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _winnerCommitted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Guid? _losingContextId;
    private int _throwBeforeNextCommit;
    private int _verificationMustWait;

    /// <summary>
    /// Arms the interceptor for the next transaction commit.
    /// </summary>
    public void Arm()
    {
        Interlocked.Exchange(
            ref _throwBeforeNextCommit,
            1);
    }

    /// <summary>
    /// Waits until the losing transaction reaches its commit boundary.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous wait.</returns>
    public async Task WaitForFirstCommitAttemptAsync(CancellationToken cancellationToken)
    {
        await _firstCommitAttempted.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Releases the losing transaction's verification after the winner committed.
    /// </summary>
    public void ReleaseVerification()
    {
        _winnerCommitted.TrySetResult();
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken)
    {

        if (Interlocked.Exchange(
            ref _throwBeforeNextCommit,
            0) == 1)
        {
            _losingContextId = eventData.Context?.ContextId.InstanceId;
            Interlocked.Exchange(
                ref _verificationMustWait,
                1);
            _firstCommitAttempted.TrySetResult();

            throw new TimeoutException(
                "PostgreSQL rolled back the transaction before acknowledging its commit.");
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken)
    {
        var isLosingVerification =
            eventData.Context?.ContextId.InstanceId == _losingContextId &&
            Volatile.Read(ref _verificationMustWait) == 1 &&
            command.CommandText.Contains(
                "authentication_sessions",
                StringComparison.OrdinalIgnoreCase);

        if (!isLosingVerification)
            return result;

        await _winnerCommitted.Task.WaitAsync(cancellationToken);
        Interlocked.Exchange(
            ref _verificationMustWait,
            0);

        return result;
    }
}
