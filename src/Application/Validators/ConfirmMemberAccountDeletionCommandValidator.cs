using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates the account deletion confirmation.</summary>
public class ConfirmMemberAccountDeletionCommandValidator : AbstractValidator<ConfirmMemberAccountDeletionCommand>
{
    /// <summary>Initializes the centralized validation rules.</summary>
    public ConfirmMemberAccountDeletionCommandValidator()
    {
        RuleFor(command => command.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.Token)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .MaximumLength(4096);
    }
}
