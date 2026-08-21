using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

internal class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
{
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
        ArgumentNullException.ThrowIfNull(formatter);
        messages.Enqueue(formatter(
            state,
            exception));
    }
}
