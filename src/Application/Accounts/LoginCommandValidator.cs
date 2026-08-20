using FluentValidation;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    private const int MaximumPasswordLength = 128;

    public LoginCommandValidator()
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
            .Must(password => password!.EnumerateRunes().Count() <= MaximumPasswordLength)
            .WithMessage($"The password must not exceed {MaximumPasswordLength} characters.");
    }
}
