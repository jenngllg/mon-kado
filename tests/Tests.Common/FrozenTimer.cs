namespace JennGllg.Fr.MonKado.Back.Tests.Common;

/// <summary>Prevents wall-clock scheduling from racing deterministic admission tests.</summary>
public class FrozenTimer : ITimer
{
    /// <inheritdoc />
    public bool Change(
        TimeSpan dueTime,
        TimeSpan period)
    {

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }
}
