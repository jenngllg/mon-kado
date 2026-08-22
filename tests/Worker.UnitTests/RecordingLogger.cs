using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class RecordingLogger<TCategory> : ILogger<TCategory>
{
    public List<(LogLevel LogLevel, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((
            logLevel,
            exception));
    }
}
