using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Changes durable account state after a commit whose acknowledgement is then lost.</summary>
public class ProfileImageCommitAccountChangeInterceptor : DbTransactionInterceptor
{
    /// <summary>Gets or sets the one-shot account change made before commit verification.</summary>
    public Func<CancellationToken, Task>? AfterCommit
    {
        get; set;
    }

    /// <inheritdoc/>
    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken)
    {
        var callback = AfterCommit;

        if (callback is null)
            return;
        AfterCommit = null;
        await callback(cancellationToken);

        throw new TimeoutException("The commit acknowledgement was lost after the account changed.");
    }
}
