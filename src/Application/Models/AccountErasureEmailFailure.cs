namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Classifies notification failures without provider text or recipient data.</summary>
public enum AccountErasureEmailFailure
{
    /// <summary>A retryable transport failure occurred.</summary>
    Transient,
    /// <summary>The provider requested a rate-limit delay.</summary>
    RateLimited,
    /// <summary>The provider rejected delivery.</summary>
    Rejected,
    /// <summary>The protected recipient could not be read.</summary>
    InvalidRecipient,
    /// <summary>An unexpected implementation failure occurred.</summary>
    Unexpected
}
