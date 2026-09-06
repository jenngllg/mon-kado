namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests;

public class ManualImportTimer(
    TimerCallback callback,
    object? state) : ITimer
{
    public void Fire()
    {
        callback(state);
    }

    public bool Change(
        TimeSpan dueTime,
        TimeSpan period)
    {

        return true;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }
}
