using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates member profile update commands.
/// </summary>
public class UpdateMemberProfileCommandValidator : AbstractValidator<UpdateMemberProfileCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateMemberProfileCommandValidator" /> class.
    /// </summary>
    public UpdateMemberProfileCommandValidator()
    {
        RuleFor(command => command.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.DisplayName)
            .ApplyDisplayNameRules();
    }
}
