namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

internal class AdvancingTimeProvider(
    DateTimeOffset currentTime,
    TimeSpan? timestampStep = null) : TimeProvider
{
    private readonly object _lock = new();
    private readonly TimeSpan _timestampStep = timestampStep ?? TimeSpan.Zero;
    private DateTimeOffset _currentTime = currentTime;
    private int _timerCreationCount;

    public int TimerCreationCount => _timerCreationCount;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {

            return _currentTime;
        }
    }

    public override long GetTimestamp()
    {
        lock (_lock)
        {
            var timestamp = _currentTime.UtcTicks;
            _currentTime = _currentTime.Add(_timestampStep);

            return timestamp;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        Interlocked.Increment(ref _timerCreationCount);

        return new AdvancingTimer(
            this,
            callback,
            state,
            dueTime);
    }

    public void Advance(TimeSpan duration)
    {
        lock (_lock)
        {
            _currentTime = _currentTime.Add(duration);
        }
    }
}
