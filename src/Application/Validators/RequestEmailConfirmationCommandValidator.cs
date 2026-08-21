using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;
/// <summary>
/// Represents request email confirmation command validator.
/// </summary>

public class RequestEmailConfirmationCommandValidator
    : AbstractValidator<RequestEmailConfirmationCommand>
{
    /// <summary>
    /// Initializes a new instance of the class.
    /// </summary>
    public RequestEmailConfirmationCommandValidator()
    {
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(EmailAddressValidation.IsWithinMaximumLength)
            .WithMessage(ValidationMessages.EmailAddressTooLong)
            .Must(EmailAddressValidation.IsValid)
            .WithMessage(ValidationMessages.InvalidEmailAddress);
    }
}
