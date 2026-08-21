using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using System.Globalization;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;
/// <summary>
/// Represents register account command validator.
/// </summary>

public class RegisterAccountCommandValidator : AbstractValidator<RegisterAccountCommand>
{
    private const int MinimumPasswordLength = 12;
    private const int MaximumPasswordLength = 128;
    private const int MaximumDisplayNameLength = 80;
    /// <summary>
    /// Initializes a new instance of the class.
    /// </summary>

    public RegisterAccountCommandValidator()
    {
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(EmailAddressValidation.IsWithinMaximumLength)
            .WithMessage(ValidationMessages.EmailAddressTooLong)
            .Must(EmailAddressValidation.IsValid)
            .WithMessage(ValidationMessages.InvalidEmailAddress);

        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(password => CountUnicodeScalars(password!) >= MinimumPasswordLength)
            .WithMessage($"The password must contain at least {MinimumPasswordLength} characters.")
            .Must(password => CountUnicodeScalars(password!) <= MaximumPasswordLength)
            .WithMessage($"The password must not exceed {MaximumPasswordLength} characters.");

        RuleFor(command => command.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(displayName => CountUnicodeScalars(displayName!.Trim()) >= 1)
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(displayName => CountUnicodeScalars(displayName!.Trim()) <= MaximumDisplayNameLength)
            .WithMessage($"The display name must not exceed {MaximumDisplayNameLength} characters.")
            .Must(displayName => NotContainControlCharacters(displayName!))
            .WithMessage("The display name must not contain control characters.");
    }

    private static bool NotContainControlCharacters(string value)
    {

        return value.EnumerateRunes().All(rune =>
            Rune.GetUnicodeCategory(rune) is not UnicodeCategory.Control and not UnicodeCategory.Surrogate);
    }

    private static int CountUnicodeScalars(string value)
    {

        return value.EnumerateRunes().Count();
    }
}
