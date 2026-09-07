using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents a sanitized delivery failure without raw provider details or nested exceptions.</summary>
/// <param name="failure">The allowlisted classification.</param>
/// <param name="retryAfter">The optional provider retry delay.</param>
public class WishlistModerationEmailDeliveryException(
    WishlistModerationEmailFailure failure,
    TimeSpan? retryAfter) : Exception("Moderation notification delivery failed.")
{
    /// <summary>Gets the bounded technical failure classification.</summary>
    public WishlistModerationEmailFailure Failure { get; } = failure;
    /// <summary>Gets the optional provider retry delay.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
