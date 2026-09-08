using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Communicates a bounded notification failure without its original exception chain.</summary>
/// <param name="failure">The technical classification.</param>
/// <param name="retryAfter">The optional provider-requested delay.</param>
public class AccountErasureEmailDeliveryException(
    AccountErasureEmailFailure failure,
    TimeSpan? retryAfter) : Exception("The erasure notification could not be delivered.")
{
    /// <summary>Gets the bounded classification.</summary>
    public AccountErasureEmailFailure Failure { get; } = failure;
    /// <summary>Gets the optional provider-requested delay.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
