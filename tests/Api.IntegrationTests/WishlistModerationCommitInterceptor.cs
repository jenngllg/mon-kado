using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Injects a one-shot moderation transaction failure before or after the actual commit.</summary>
public class WishlistModerationCommitInterceptor : AmbiguousCommitInterceptor
{
    private int _failBeforeCommit;
    /// <summary>Arms a failure before the next transaction is committed.</summary>
    public void ArmBeforeCommit()
    {
        Interlocked.Exchange(
            ref _failBeforeCommit,
            1);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken)
    {

        if (Interlocked.Exchange(
            ref _failBeforeCommit,
            0) == 1)
            throw new TimeoutException("The moderation transaction failed before commit.");

        return ValueTask.FromResult(result);
    }
}
