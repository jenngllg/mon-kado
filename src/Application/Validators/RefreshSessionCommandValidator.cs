using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates authentication session rotation commands.
/// </summary>
public class RefreshSessionCommandValidator : AbstractValidator<RefreshSessionCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshSessionCommandValidator" /> class.
    /// </summary>
    public RefreshSessionCommandValidator()
    {
        RuleFor(command => command.RefreshToken)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
