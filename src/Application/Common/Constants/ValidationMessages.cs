using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;
/// <summary>
/// Represents validation messages.
/// </summary>

[ExcludeFromCodeCoverage]
public static class ValidationMessages
{
    /// <summary>
    /// Identifies mandatory property.
    /// </summary>
    public const string MandatoryProperty = "The property {PropertyName} is mandatory.";
    /// <summary>
    /// Identifies invalid email address.
    /// </summary>
    public const string InvalidEmailAddress = "The email address is invalid.";
    /// <summary>
    /// Identifies email address too long.
    /// </summary>
    public const string EmailAddressTooLong = "The email address must not exceed 254 characters.";
    /// <summary>
    /// Identifies invalid email confirmation link.
    /// </summary>
    public const string InvalidEmailConfirmationLink = "The email confirmation link is invalid.";
    /// <summary>
    /// Identifies an invalid member email change confirmation link.
    /// </summary>
    public const string InvalidEmailChangeConfirmationLink = "The email change confirmation link is invalid.";
    /// <summary>
    /// Identifies an invalid password reset link.
    /// </summary>
    public const string InvalidPasswordResetLink = "The password reset link is invalid.";
    /// <summary>
    /// Identifies a password below the minimum supported length.
    /// </summary>
    public const string PasswordTooShort = "The password must contain at least 12 characters.";
    /// <summary>
    /// Identifies a password above the maximum supported length.
    /// </summary>
    public const string PasswordTooLong = "The password must not exceed 128 characters.";
    /// <summary>
    /// Identifies a new password that is identical to the current password.
    /// </summary>
    public const string NewPasswordMustDiffer = "The new password must differ from the current password.";
}
