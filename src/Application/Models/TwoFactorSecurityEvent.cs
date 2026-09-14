namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Identifies a completed authenticator security operation without exposing credentials.</summary>
public enum TwoFactorSecurityEvent
{
    /// <summary>The first authenticator was confirmed.</summary>
    Enrolled,
    /// <summary>A replacement authenticator was confirmed.</summary>
    Replaced,
    /// <summary>A new recovery-code set replaced the previous set.</summary>
    RecoveryCodesRegenerated,
    /// <summary>A recovery code authorized a forced replacement flow.</summary>
    RecoveryCodeUsed
}
