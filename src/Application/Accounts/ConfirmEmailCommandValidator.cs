using FluentValidation;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    private const int MaximumTokenLength = 2048;

    public ConfirmEmailCommandValidator()
    {
        RuleFor(command => command.UserId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(BeNonEmptyCanonicalGuid)
            .WithMessage("The email confirmation link is invalid.");

        RuleFor(command => command.Token)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(MaximumTokenLength)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("The email confirmation link is invalid.");
    }

    private static bool BeNonEmptyCanonicalGuid(string? value)
    {
        return Guid.TryParseExact(value, "D", out Guid userId) && userId != Guid.Empty;
    }
}
