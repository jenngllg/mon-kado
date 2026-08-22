using System.Net.Mail;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Provides reusable email address validation rules.
/// </summary>
public static class EmailAddressValidation
{
    /// <summary>
    /// Identifies maximum length.
    /// </summary>
    public const int MaximumLength = 254;
    /// <summary>
    /// Executes the is within maximum length operation.
    /// </summary>
    /// <param name="email">The email.</param>
    /// <returns>The operation result.</returns>

    public static bool IsWithinMaximumLength(string? email)
    {

        return email is not null && email.Trim().EnumerateRunes().Count() <= MaximumLength;
    }
    /// <summary>
    /// Executes the is valid operation.
    /// </summary>
    /// <param name="email">The email.</param>
    /// <returns>The operation result.</returns>

    public static bool IsValid(string? email)
    {
        var candidate = email?.Trim() ?? string.Empty;

        return MailAddress.TryCreate(
            candidate,
            out var address) &&
            string.Equals(
                address.Address,
                candidate,
                StringComparison.OrdinalIgnoreCase);
    }
}
