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
    PasswordChangedSecurityNotification
}
