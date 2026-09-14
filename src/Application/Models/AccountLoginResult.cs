namespace JennGllg.Fr.MonKado.Back.Application.Models;
/// <summary>
/// Defines the available account login result values.
/// </summary>

public enum AccountLoginResult
{
    /// <summary>
    /// Indicates success.
    /// </summary>
    Success,
    /// <summary>
    /// Indicates invalid credentials.
    /// </summary>
    InvalidCredentials,
    /// <summary>
    /// Indicates email not confirmed.
    /// </summary>
    EmailNotConfirmed,
    /// <summary>Indicates that the first factor succeeded but a second factor remains mandatory.</summary>
    TwoFactorRequired
}
