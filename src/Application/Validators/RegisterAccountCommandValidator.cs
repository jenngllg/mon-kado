using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;
/// <summary>
/// Represents register account command validator.
/// </summary>

public class RegisterAccountCommandValidator : AbstractValidator<RegisterAccountCommand>
{
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
            .ApplyNewPasswordRules();

        RuleFor(command => command.DisplayName)
            .ApplyDisplayNameRules();
    }

}
