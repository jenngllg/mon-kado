using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public ILogger CreateLogger(string categoryName)
    {

        return new CapturingLogger(
            _messages,
            categoryName);
    }

    public void Dispose()
    {
    }
}
