namespace JennGllg.Fr.MonKado.Back.Tests.Common;

/// <summary>Keeps timer deadlines frozen while preserving real UTC for token validation.</summary>
public class FrozenTimerTimeProvider : TimeProvider
{
    /// <inheritdoc />
    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {

        return new FrozenTimer();
    }
}
