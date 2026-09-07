using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data;
using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Commits a concurrent report after the page count snapshot has been established.</summary>
public class ReportedWishlistReadInterceptor(Func<CancellationToken, Task> afterCount) : DbCommandInterceptor
{
    public bool Triggered
    {
        get; private set;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken)
    {
        Assert.NotNull(command.Transaction);
        Assert.Equal(
            IsolationLevel.RepeatableRead,
            command.Transaction.IsolationLevel);

        if (!Triggered && command.CommandText.Contains(
            "count(*)",
            StringComparison.OrdinalIgnoreCase))
        {
            Triggered = true;
            await afterCount(cancellationToken);
        }

        return result;
    }
}
