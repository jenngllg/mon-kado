namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests;

public class ManualImportTimeProvider : TimeProvider
{
    public ManualImportTimer? Timer
    {
        get; private set;
    }
    public TimeSpan? DueTime
    {
        get; private set;
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        DueTime = dueTime;
        Timer = new ManualImportTimer(
            callback,
            state);

        return Timer;
    }
}
