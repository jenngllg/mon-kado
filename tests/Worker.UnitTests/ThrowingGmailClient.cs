using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ThrowingGmailClient(Exception exception) : IGmailApiClient
{
    public Task<string> SendAsync(
        string rawMessage,
        CancellationToken cancellationToken)
    {

        return Task.FromException<string>(exception);
    }
}
