namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

/// <summary>Signals the end of a cleanup cycle without advancing real time.</summary>
public class CleanupCycleTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly TaskCompletionSource<TimeSpan> _cycleCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Gets a signal raised when the worker schedules its next cycle.</summary>
    public Task<TimeSpan> CycleCompleted => _cycleCompleted.Task;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow()
    {

        return now;
    }

    /// <inheritdoc/>
    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        _cycleCompleted.TrySetResult(dueTime);

        return new Timer(
            callback,
            state,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }
}
