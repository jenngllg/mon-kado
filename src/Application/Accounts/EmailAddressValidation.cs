using System.Net.Mail;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

internal static class EmailAddressValidation
{
    public const int MaximumLength = 254;

    public static bool IsWithinMaximumLength(string? email)
    {
        return email is not null && email.Trim().EnumerateRunes().Count() <= MaximumLength;
    }

    public static bool IsValid(string? email)
    {
        string candidate = email?.Trim() ?? string.Empty;
        return MailAddress.TryCreate(candidate, out MailAddress? address) &&
            string.Equals(address.Address, candidate, StringComparison.OrdinalIgnoreCase);
    }
}
