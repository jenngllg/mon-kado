using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Simulates PostgreSQL becoming unreachable during independent commit verification.</summary>
public class AccountDeletionVerificationFailure : DbConnectionInterceptor
{
    public bool Unavailable
    {
        get; set;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken)
    {

        if (Unavailable)
            throw new TimeoutException("PostgreSQL is unavailable during commit verification.");

        return ValueTask.FromResult(result);
    }
}
