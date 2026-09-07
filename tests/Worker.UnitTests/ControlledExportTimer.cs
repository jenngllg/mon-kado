namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ControlledExportTimer(
    TimerCallback callback,
    object? state) : ITimer
{
    private int _disposed;
    public void Fire()
    {

        if (Volatile.Read(ref _disposed) == 0)
            callback(state);
    }

    public bool Change(
        TimeSpan dueTime,
        TimeSpan period) => Volatile.Read(ref _disposed) == 0;
    public void Dispose()
    {
        Interlocked.Exchange(
            ref _disposed,
            1);
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();

        return ValueTask.CompletedTask;
    }
}
