using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Simulates a database outage on exactly one explicitly armed read.</summary>
public class FailNextDatabaseReadInterceptor : DbCommandInterceptor
{
    private int _armed;

    /// <summary>Arms a failure for the next asynchronous reader command.</summary>
    public void Arm()
    {
        Interlocked.Exchange(
            ref _armed,
            1);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken)
    {

        if (Interlocked.Exchange(
                ref _armed,
                0) == 1)
            throw new TimeoutException("The database read could not be completed.");

        return ValueTask.FromResult(result);
    }
}
