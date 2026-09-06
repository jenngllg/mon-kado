using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class RecordingLogger<TCategory> : ILogger<TCategory>
{
    public List<string?> TraceIdsAtLog { get; } = [];

    public List<(LogLevel LogLevel, EventId EventId, Exception? Exception, string Message)> Entries
    {
        get;
    } = [];

    public List<IReadOnlyDictionary<string, object>> Scopes
    {
        get;
    } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        var properties = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object>>>(state)
            .ToDictionary(
                property => property.Key,
                property => property.Value,
                StringComparer.Ordinal);
        Scopes.Add(properties);

        return new RecordingLogScope();
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
        TraceIdsAtLog.Add(System.Diagnostics.Activity.Current?.TraceId.ToString());
        Entries.Add((
            logLevel,
            eventId,
            exception,
            formatter(
                state,
                exception)));
    }
}
