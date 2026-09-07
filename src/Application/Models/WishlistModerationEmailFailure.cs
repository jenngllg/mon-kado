namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Defines the complete allowlist of moderation delivery failure classifications.</summary>
public enum WishlistModerationEmailFailure
{
    /// <summary>A network or provider failure may be retried.</summary>
    Transient,
    /// <summary>The provider has temporarily exhausted its quota.</summary>
    RateLimited,
    /// <summary>The provider rejected authentication, permissions or message input.</summary>
    Rejected,
    /// <summary>An unexpected failure occurred, without disclosing its private details.</summary>
    Unexpected
}
