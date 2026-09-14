namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>
/// Defines the available authentication email kind values.
/// </summary>
public enum AuthenticationEmailKind
{
    /// <summary>
    /// Indicates email confirmation.
    /// </summary>
    EmailConfirmation,
    /// <summary>
    /// Indicates a member email change confirmation sent to the new address.
    /// </summary>
    EmailChangeConfirmation,
    /// <summary>
    /// Indicates a member email change security notification sent to the current address.
    /// </summary>
    EmailChangeSecurityNotification,
    /// <summary>
    /// Indicates a member password reset link.
    /// </summary>
    PasswordReset,
    /// <summary>
    /// Indicates a security notification sent after a member password change.
    /// </summary>
    PasswordChangedSecurityNotification,
    /// <summary>Indicates an account deletion confirmation sent to the current address.</summary>
    AccountDeletionConfirmation,
    /// <summary>Indicates that a private personal-data archive is ready to download.</summary>
    PersonalDataExportReady,
    /// <summary>Indicates a confirmed first authenticator.</summary>
    TwoFactorEnrolled,
    /// <summary>Indicates a confirmed replacement authenticator.</summary>
    TwoFactorReplaced,
    /// <summary>Indicates replacement of the entire recovery-code set.</summary>
    TwoFactorRecoveryCodesRegenerated,
    /// <summary>Indicates use of a recovery code to authorize forced replacement.</summary>
    TwoFactorRecoveryCodeUsed
}
