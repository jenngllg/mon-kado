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
}
