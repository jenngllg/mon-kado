using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates password reset email requests.
/// </summary>
public class RequestPasswordResetCommandValidator
    : AbstractValidator<RequestPasswordResetCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestPasswordResetCommandValidator" /> class.
    /// </summary>
    public RequestPasswordResetCommandValidator()
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
