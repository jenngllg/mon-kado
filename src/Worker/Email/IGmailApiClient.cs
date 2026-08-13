namespace JennGllg.Fr.MonKado.Back.Worker.Email;

internal interface IGmailApiClient
{
    Task<string> SendAsync(string rawMessage, CancellationToken cancellationToken);
}

internal sealed class GmailRequestException(
    System.Net.HttpStatusCode? statusCode,
    TimeSpan? retryAfter,
    Exception? innerException = null)
    : Exception("The Gmail API request failed.", innerException)
{
    public System.Net.HttpStatusCode? StatusCode { get; } = statusCode;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
