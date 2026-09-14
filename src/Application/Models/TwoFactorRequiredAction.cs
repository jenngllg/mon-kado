namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Identifies the next permitted step of a second-factor sign-in.</summary>
public enum TwoFactorRequiredAction
{
    /// <summary>Verify the existing authenticator or begin recovery.</summary>
    Verify,
    /// <summary>Enroll the first authenticator.</summary>
    Enroll,
    /// <summary>Replace the existing authenticator.</summary>
    Replace,
    /// <summary>Finish a sign-in whose second factor was already confirmed.</summary>
    Complete
}
