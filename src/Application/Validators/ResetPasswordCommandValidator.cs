using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates anonymous password reset commands.
/// </summary>
public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    private const int MaximumTokenLength = 2048;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetPasswordCommandValidator" /> class.
    /// </summary>
    public ResetPasswordCommandValidator()
    {
        RuleFor(command => command.UserId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(BeNonEmptyCanonicalGuid)
            .WithMessage(ValidationMessages.InvalidPasswordResetLink);
        RuleFor(command => command.Token)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(MaximumTokenLength)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage(ValidationMessages.InvalidPasswordResetLink);
        RuleFor(command => command.NewPassword)
            .ApplyNewPasswordRules();
    }

    private static bool BeNonEmptyCanonicalGuid(string? value)
    {

        return Guid.TryParseExact(
            value,
            "D",
            out var userId) && userId != Guid.Empty;
    }
}
