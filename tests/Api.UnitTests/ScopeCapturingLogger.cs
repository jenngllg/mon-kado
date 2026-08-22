using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class ScopeCapturingLogger<TCategory> : ILogger<TCategory>
{
    public IReadOnlyDictionary<string, object>? Scope
    {
        get; private set;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        Scope = state as IReadOnlyDictionary<string, object>;

        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {

        return false;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
