using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Injects a one-shot failure before or after a gift-image transaction commits.</summary>
public class GiftImageCommitInterceptor : AmbiguousCommitInterceptor
{
    private int _failBeforeCommit;

    /// <summary>Arms a failure before PostgreSQL commits the next transaction.</summary>
    public void ArmBeforeCommit()
    {
        Interlocked.Exchange(
            ref _failBeforeCommit,
            1);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(
                ref _failBeforeCommit,
                0) == 1)
            throw new TimeoutException("The transaction failed before commit.");

        return ValueTask.FromResult(result);
    }
}
