using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates member email change requests.
/// </summary>
public class RequestMemberEmailChangeCommandValidator
    : AbstractValidator<RequestMemberEmailChangeCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestMemberEmailChangeCommandValidator" /> class.
    /// </summary>
    public RequestMemberEmailChangeCommandValidator()
    {
        RuleFor(command => command.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(EmailAddressValidation.IsWithinMaximumLength)
            .WithMessage(ValidationMessages.EmailAddressTooLong)
            .Must(EmailAddressValidation.IsValid)
            .WithMessage(ValidationMessages.InvalidEmailAddress);
        RuleFor(command => command.CurrentPassword)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
