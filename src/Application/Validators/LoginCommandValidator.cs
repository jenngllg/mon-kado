using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;
/// <summary>
/// Represents login command validator.
/// </summary>

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    /// <summary>
    /// Initializes a new instance of the class.
    /// </summary>

    public LoginCommandValidator()
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
            .ApplySubmittedPasswordRules();
    }
}
