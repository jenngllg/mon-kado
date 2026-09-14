using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates RegenerateTwoFactorRecoveryCodesCommand before accessing security state or consuming proof.</summary>
public class RegenerateTwoFactorRecoveryCodesCommandValidator : AbstractValidator<RegenerateTwoFactorRecoveryCodesCommand>
{
    /// <summary>Initializes the centralized second-factor request rules.</summary>
    public RegenerateTwoFactorRecoveryCodesCommandValidator()
    {
        RuleFor(request => request.Flow)
            .Must(TwoFactorInputValidation.IsFlow)
            .WithMessage(ValidationMessages.InvalidTwoFactorFlow);
    }
}
