using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Loses a commit acknowledgement and prevents its independent verification.</summary>
public class AccountDeletionLostCommitInterceptor(AccountDeletionVerificationFailure failure) : DbTransactionInterceptor
{
    public bool Armed
    {
        get; set;
    }

    /// <inheritdoc/>
    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken)
    {

        if (Armed)
        {
            Armed = false;
            failure.Unavailable = true;

            throw new TimeoutException("The commit acknowledgement was lost.");
        }

        return Task.CompletedTask;
    }
}
