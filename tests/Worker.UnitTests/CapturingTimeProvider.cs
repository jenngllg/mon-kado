namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class CapturingTimeProvider(
    DateTimeOffset now,
    CancellationTokenSource cancellationSource) : TimeProvider
{
    public TimeSpan? DueTime
    {
        get; private set;
    }

    public override DateTimeOffset GetUtcNow()
    {

        return now;
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        DueTime = dueTime;
        cancellationSource.Cancel();

        return new ImmediateTimer();
    }
}
