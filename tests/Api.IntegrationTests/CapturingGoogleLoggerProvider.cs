using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>
/// Captures structured Google authentication log events for integration assertions.
/// </summary>
public class CapturingGoogleLoggerProvider : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<KeyValuePair<int, string>> _entries = new();

    /// <summary>
    /// Gets the captured event identifiers and rendered messages.
    /// </summary>
    public IReadOnlyCollection<KeyValuePair<int, string>> Entries => _entries;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {

        return this;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {

        return null;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {

        return true;
    }

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Enqueue(new KeyValuePair<int, string>(
            eventId.Id,
            formatter(
                state,
                exception)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
