namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests.Services;

public class LeaseContentionTimer : ITimer
{
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
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }
}
