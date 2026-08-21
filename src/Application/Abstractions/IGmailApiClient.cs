namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;
/// <summary>
/// Defines the contract for gmail api client.
/// </summary>

public interface IGmailApiClient
{
    /// <summary>
    /// Executes the send async operation.
    /// </summary>
    /// <param name="rawMessage">The raw message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<string> SendAsync(
        string rawMessage,
        CancellationToken cancellationToken);
}
