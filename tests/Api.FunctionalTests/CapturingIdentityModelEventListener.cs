using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class CapturingIdentityModelEventListener : EventListener
{
    private const string IdentityModelEventSourceName = "Microsoft.IdentityModel.EventSource";
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {

        if (string.Equals(
            eventSource.Name,
            IdentityModelEventSourceName,
            StringComparison.Ordinal))
            EnableEvents(
                eventSource,
                EventLevel.Verbose);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        var payload = eventData.Payload is null
            ? string.Empty
            : string.Join(
                " | ",
                eventData.Payload.Select(value => value?.ToString()));
        _messages.Enqueue(string.Concat(
            eventData.Message,
            " | ",
            payload));
    }
}
