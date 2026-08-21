namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ImmediateTimer : ITimer
{
    public bool Change(
        TimeSpan dueTime,
        TimeSpan period)
    {

        return true;
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {

        return ValueTask.CompletedTask;
    }
}
