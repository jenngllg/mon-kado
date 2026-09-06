namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests.Services;

public class LeaseContentionTimeProvider(Action onDelay) : TimeProvider
{
    public TimeSpan Delay
    {
        get; private set;
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        Delay = dueTime;
        onDelay();
        callback(state);

        return new LeaseContentionTimer();
    }
}
