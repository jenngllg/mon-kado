using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates member email change confirmations.
/// </summary>
public class ConfirmMemberEmailChangeCommandValidator
    : AbstractValidator<ConfirmMemberEmailChangeCommand>
{
    private const int MaximumTokenLength = 2048;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmMemberEmailChangeCommandValidator" /> class.
    /// </summary>
    public ConfirmMemberEmailChangeCommandValidator()
    {
        RuleFor(command => command.RequestId)
            .NotEmpty()
            .WithMessage(ValidationMessages.InvalidEmailChangeConfirmationLink);
        RuleFor(command => command.Token)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(MaximumTokenLength)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage(ValidationMessages.InvalidEmailChangeConfirmationLink);
    }
}
