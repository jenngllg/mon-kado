namespace JennGllg.Fr.MonKado.Back.Worker.Exceptions;
/// <summary>
/// Represents gmail request exception.
/// </summary>
/// <param name="statusCode">The status code.</param>
/// <param name="retryAfter">The retry after.</param>
/// <param name="innerException">The inner exception.</param>

public class GmailRequestException(
    System.Net.HttpStatusCode? statusCode,
    TimeSpan? retryAfter,
    Exception? innerException = null)
    : Exception(
        "The Gmail API request failed.",
        innerException)
{
    /// <summary>
    /// Gets status code.
    /// </summary>
    public System.Net.HttpStatusCode? StatusCode { get; } = statusCode;
    /// <summary>
    /// Gets retry after.
    /// </summary>

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
