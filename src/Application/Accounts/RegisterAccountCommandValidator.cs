using System.Globalization;
using System.Text;
using FluentValidation;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed class RegisterAccountCommandValidator : AbstractValidator<RegisterAccountCommand>
{
    private const int MinimumPasswordLength = 12;
    private const int MaximumPasswordLength = 128;
    private const int MaximumDisplayNameLength = 80;

    public RegisterAccountCommandValidator()
    {
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("The email address is required.")
            .Must(EmailAddressValidation.IsWithinMaximumLength)
            .WithMessage($"The email address must not exceed {EmailAddressValidation.MaximumLength} characters.")
            .Must(EmailAddressValidation.IsValid)
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

    private static bool NotContainControlCharacters(string? value)
    {
        return value is not null && value.EnumerateRunes().All(rune =>
            Rune.GetUnicodeCategory(rune) is not UnicodeCategory.Control and not UnicodeCategory.Surrogate);
    }

    private static int CountUnicodeScalars(string value) => value.EnumerateRunes().Count();
}
