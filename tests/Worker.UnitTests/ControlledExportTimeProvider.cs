using System.Collections.Concurrent;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ControlledExportTimeProvider : TimeProvider
{
    private readonly ConcurrentDictionary<TimeSpan, TaskCompletionSource<ControlledExportTimer>> _timers = new();
    public Task<ControlledExportTimer> WaitForTimerAsync(
        TimeSpan dueTime,
        CancellationToken cancellationToken)
    {

        return _timers
            .GetOrAdd(
            dueTime,
            _ => new(TaskCreationOptions.RunContinuationsAsynchronously))
            .Task.WaitAsync(cancellationToken);
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new ControlledExportTimer(
            callback,
            state);
        _timers
            .GetOrAdd(
            dueTime,
            _ => new(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult(timer);

        return timer;
    }
}
