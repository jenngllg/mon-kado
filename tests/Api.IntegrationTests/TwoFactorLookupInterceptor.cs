using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Coordinates a concurrent security change after the flow-owner lookup but before account locking.</summary>
/// <param name="afterLookup">The independently committed security change.</param>
public class TwoFactorLookupInterceptor(Func<CancellationToken, Task> afterLookup) : DbCommandInterceptor
{
    private int _invoked;

    /// <inheritdoc />
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken)
    {

        if (command.CommandText.StartsWith(
                "SELECT t.member_id",
                StringComparison.Ordinal) &&
            command.CommandText.Contains(
                "two_factor_challenges",
                StringComparison.Ordinal) &&
            Interlocked.Exchange(
                ref _invoked,
                1) == 0)
            await afterLookup(cancellationToken);

        return result;
    }
}
