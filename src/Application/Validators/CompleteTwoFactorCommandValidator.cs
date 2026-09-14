using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates CompleteTwoFactorCommand before accessing security state or consuming proof.</summary>
public class CompleteTwoFactorCommandValidator : AbstractValidator<CompleteTwoFactorCommand>
{
    /// <summary>Initializes the centralized second-factor request rules.</summary>
    public CompleteTwoFactorCommandValidator()
    {
        RuleFor(request => request.Flow)
            .Must(TwoFactorInputValidation.IsFlow)
            .WithMessage(ValidationMessages.InvalidTwoFactorFlow);
        RuleFor(request => request.Code)
            .Must(code => code is null || TwoFactorInputValidation.IsCode(code))
            .WithMessage(ValidationMessages.InvalidTwoFactorCode);
        RuleFor(request => request.RecoveryCode)
            .Must(code => code is null || TwoFactorInputValidation.IsRecoveryCode(code))
            .WithMessage(ValidationMessages.InvalidTwoFactorRecoveryCode);
        RuleFor(request => request.Code)
            .Must((
                request,
                code) => code is null || request.RecoveryCode is null)
            .WithMessage(ValidationMessages.TwoFactorProofsAreExclusive);
    }
}
