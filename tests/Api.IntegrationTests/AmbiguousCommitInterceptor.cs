using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>
/// Injects a single transient failure after PostgreSQL has committed a transaction.
/// </summary>
public class AmbiguousCommitInterceptor : DbTransactionInterceptor
{
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

    /// <inheritdoc />
    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken)
    {

        if (Interlocked.Exchange(
            ref _throwAfterNextCommit,
            0) == 1)
        {

            throw new TimeoutException(
                "The commit acknowledgement was lost after PostgreSQL committed the transaction.");
        }

        return Task.CompletedTask;
    }
}
