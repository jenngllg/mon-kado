using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Tests.Common;

/// <summary>Captures messages and complete exception text to verify log confidentiality.</summary>
public class RecordingExceptionLogger<T> : ILogger<T>
{
    /// <summary>Gets rendered log entries, including exception chains.</summary>
    public List<string> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
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
        Entries.Add(formatter(
            state,
            exception) + exception?.ToString());
    }
}
