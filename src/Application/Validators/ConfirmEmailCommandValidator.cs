using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;
/// <summary>
/// Represents confirm email command validator.
/// </summary>

public class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    private const int MaximumTokenLength = 2048;
    /// <summary>
    /// Initializes a new instance of the class.
    /// </summary>

    public ConfirmEmailCommandValidator()
    {
        RuleFor(command => command.UserId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(BeNonEmptyCanonicalGuid)
            .WithMessage(ValidationMessages.InvalidEmailConfirmationLink);

        RuleFor(command => command.Token)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(MaximumTokenLength)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage(ValidationMessages.InvalidEmailConfirmationLink);
    }

    private static bool BeNonEmptyCanonicalGuid(string? value)
    {

        return Guid.TryParseExact(
            value,
            "D",
            out var userId) && userId != Guid.Empty;
    }
}
