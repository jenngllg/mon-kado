using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
/// <summary>
/// Represents authentication email delivery exception.
/// </summary>
/// <param name="category">The category.</param>
/// <param name="retryAfter">The retry after.</param>
/// <param name="innerException">The inner exception.</param>

public class AuthenticationEmailDeliveryException(
    AuthenticationEmailFailureCategory category,
    TimeSpan? retryAfter = null,
    Exception? innerException = null)
    : Exception(
        "The authentication email provider rejected or could not process the message.",
        innerException)
{
    /// <summary>
    /// Gets category.
    /// </summary>
    public AuthenticationEmailFailureCategory Category { get; } = category;
    /// <summary>
    /// Gets retry after.
    /// </summary>

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
