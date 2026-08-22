namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ImmediateTimeProvider(DateTimeOffset? now = null) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {

        return now ?? base.GetUtcNow();
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        callback(state);

        return new ImmediateTimer();
    }
}
