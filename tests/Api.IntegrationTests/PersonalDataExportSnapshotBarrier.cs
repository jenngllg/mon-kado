using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Pauses a snapshot before reading wishes, after its account query has established the database snapshot.</summary>
public class PersonalDataExportSnapshotBarrier : DbCommandInterceptor
{
    private int _armed;
    /// <summary>Gets the signal raised after the database snapshot has been established.</summary>
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Gets the signal allowing the snapshot reader to continue.</summary>
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Arms the barrier for the next wish projection.</summary>
    public void Arm() => Interlocked.Exchange(
        ref _armed,
        1);
    /// <inheritdoc/>
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken)
    {

        if (command.CommandText.Contains(
            "FROM public.wishes AS",
            StringComparison.Ordinal) && Interlocked.Exchange(
            ref _armed,
            0) == 1)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}
