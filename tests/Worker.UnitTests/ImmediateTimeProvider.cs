namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ImmediateTimeProvider : TimeProvider
{
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
