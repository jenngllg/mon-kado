namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Defines the possible outcomes of an explicit Google account link.
/// </summary>
public enum GoogleAccountLinkOutcome
{
    /// <summary>
    /// Indicates that the Google login and MonKado session were created.
    /// </summary>
    Success,

    /// <summary>
    /// Indicates that the local account could not be proven.
    /// </summary>
    InvalidCredentials,

    /// <summary>
    /// Indicates that concurrent account state prevents an unambiguous link.
    /// </summary>
    Conflict
}
