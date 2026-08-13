using FluentValidation;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed class RequestEmailConfirmationCommandValidator
    : AbstractValidator<RequestEmailConfirmationCommand>
{
    public RequestEmailConfirmationCommandValidator()
    {
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("The email address is required.")
            .Must(EmailAddressValidation.IsWithinMaximumLength)
            .WithMessage($"The email address must not exceed {EmailAddressValidation.MaximumLength} characters.")
            .Must(EmailAddressValidation.IsValid)
            .WithMessage("The email address is invalid.");
    }
}
