using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates ConfirmTwoFactorSetupCommand before accessing security state or consuming proof.</summary>
public class ConfirmTwoFactorSetupCommandValidator : AbstractValidator<ConfirmTwoFactorSetupCommand>
{
    /// <summary>Initializes the centralized second-factor request rules.</summary>
    public ConfirmTwoFactorSetupCommandValidator()
    {
        RuleFor(request => request.Flow)
            .Must(TwoFactorInputValidation.IsFlow)
            .WithMessage(ValidationMessages.InvalidTwoFactorFlow);
        RuleFor(request => request.Code)
            .Must(TwoFactorInputValidation.IsCode)
            .WithMessage(ValidationMessages.InvalidTwoFactorCode);
    }
}
