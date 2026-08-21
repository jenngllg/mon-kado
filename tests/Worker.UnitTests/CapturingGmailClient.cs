using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class CapturingGmailClient : IGmailApiClient
{
    public string? RawMessage
    {
        get; private set;
    }

    public Task<string> SendAsync(
        string rawMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RawMessage = rawMessage;

        return Task.FromResult("gmail-message-id");
    }
}
