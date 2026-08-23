using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates current member password updates.
/// </summary>
public class UpdateMemberPasswordCommandValidator
    : AbstractValidator<UpdateMemberPasswordCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateMemberPasswordCommandValidator" /> class.
    /// </summary>
    public UpdateMemberPasswordCommandValidator()
    {
        RuleFor(command => command.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.CurrentPassword)
            .ApplySubmittedPasswordRules();
        RuleFor(command => command.NewPassword)
            .ApplyNewPasswordRules()
            .Must((
                command,
                newPassword) => !string.Equals(
                    command.CurrentPassword,
                    newPassword,
                    StringComparison.Ordinal))
            .WithMessage(ValidationMessages.NewPasswordMustDiffer);
    }
}
