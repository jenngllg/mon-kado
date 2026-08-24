using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

internal class CapturingLogger(
    ConcurrentQueue<string> messages,
    string categoryName) : ILogger
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
        var message = formatter(
            state,
            exception);

        if (exception is not null)
            message = string.Concat(
                message,
                Environment.NewLine,
                exception);

        messages.Enqueue(string.Concat(
            categoryName,
            ": [",
            logLevel,
            "] ",
            message));
    }
}
