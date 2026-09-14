namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Restricts a verified second-factor grant to one operation.</summary>
public enum TwoFactorFlowPurpose
{
    /// <summary>Complete a first-factor-authenticated sign-in.</summary>
    SignIn,
    /// <summary>Replace an authenticator from a fully authenticated session.</summary>
    ReplaceAuthenticator,
    /// <summary>Regenerate recovery codes from a fully authenticated session.</summary>
    RegenerateRecoveryCodes
}
