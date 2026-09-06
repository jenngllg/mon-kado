using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Deterministically holds one armed transaction immediately before its commit.</summary>
public class AccountDeletionCommitBarrier : DbTransactionInterceptor
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _armed;
    public void Arm() => Interlocked.Exchange(
        ref _armed,
        1);
    public Task WaitUntilEnteredAsync(CancellationToken cancellationToken) => _entered.Task.WaitAsync(cancellationToken);
    public void Release() => _released.TrySetResult();
    /// <inheritdoc/>
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken)
    {

        if (Interlocked.Exchange(
            ref _armed,
            0) == 1)
        {
            _entered.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}
