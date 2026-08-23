namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

internal class AdvancingTimer : ITimer
{
    private readonly AdvancingTimeProvider _timeProvider;
    private readonly TimerCallback _callback;
    private readonly object? _state;
    private bool _disposed;

    public AdvancingTimer(
        AdvancingTimeProvider timeProvider,
        TimerCallback callback,
        object? state,
        TimeSpan dueTime)
    {
        _timeProvider = timeProvider;
        _callback = callback;
        _state = state;
        Fire(dueTime);
    }

    public bool Change(
        TimeSpan dueTime,
        TimeSpan period)
    {

        if (_disposed)
            return false;

        Fire(dueTime);

        return true;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();

        return ValueTask.CompletedTask;
    }

    private void Fire(TimeSpan dueTime)
    {

        if (dueTime == Timeout.InfiniteTimeSpan)
            return;

        if (dueTime > TimeSpan.Zero)
            _timeProvider.Advance(dueTime);

        _callback(_state);
    }
}
