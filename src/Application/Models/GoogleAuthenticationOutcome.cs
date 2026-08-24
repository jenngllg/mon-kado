namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Defines the possible outcomes of Google authentication completion.
/// </summary>
public enum GoogleAuthenticationOutcome
{
    /// <summary>
    /// Indicates that a MonKado refresh session was created.
    /// </summary>
    SessionCreated,

    /// <summary>
    /// Indicates that the existing MonKado account must be proven with its password.
    /// </summary>
    ExplicitLinkRequired,

    /// <summary>
    /// Indicates that a non-authoritative email requires a generic MonKado verification or linking path.
    /// </summary>
    AdditionalVerificationRequired
}
