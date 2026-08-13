using System.Globalization;
using System.Net.Mail;
using System.Text;
using FluentValidation;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed class RegisterAccountCommandValidator : AbstractValidator<RegisterAccountCommand>
{
    private const int MaximumEmailLength = 254;
    private const int MinimumPasswordLength = 12;
    private const int MaximumPasswordLength = 128;
    private const int MaximumDisplayNameLength = 80;

    public RegisterAccountCommandValidator()
    {
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("The email address is required.")
            .Must(email => CountUnicodeScalars(email!.Trim()) <= MaximumEmailLength)
            .WithMessage($"The email address must not exceed {MaximumEmailLength} characters.")
            .Must(BeValidEmailAddress)
            .WithMessage("The email address is invalid.");

        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("The password is required.")
            .Must(password => CountUnicodeScalars(password!) >= MinimumPasswordLength)
            .WithMessage($"The password must contain at least {MinimumPasswordLength} characters.")
            .Must(password => CountUnicodeScalars(password!) <= MaximumPasswordLength)
            .WithMessage($"The password must not exceed {MaximumPasswordLength} characters.");

        RuleFor(command => command.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("The display name is required.")
            .Must(displayName => CountUnicodeScalars(displayName!.Trim()) >= 1)
            .WithMessage("The display name is required.")
            .Must(displayName => CountUnicodeScalars(displayName!.Trim()) <= MaximumDisplayNameLength)
            .WithMessage($"The display name must not exceed {MaximumDisplayNameLength} characters.")
            .Must(NotContainControlCharacters)
            .WithMessage("The display name must not contain control characters.");
    }

    private static bool BeValidEmailAddress(string? email)
    {
        string candidate = email?.Trim() ?? string.Empty;
        return MailAddress.TryCreate(candidate, out MailAddress? address) &&
            string.Equals(address.Address, candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NotContainControlCharacters(string? value)
    {
        return value is not null && value.EnumerateRunes().All(rune =>
            Rune.GetUnicodeCategory(rune) is not UnicodeCategory.Control and not UnicodeCategory.Surrogate);
    }

    private static int CountUnicodeScalars(string value) => value.EnumerateRunes().Count();
}
