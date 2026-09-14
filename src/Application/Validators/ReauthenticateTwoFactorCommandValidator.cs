using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates ReauthenticateTwoFactorCommand before accessing security state or consuming proof.</summary>
public class ReauthenticateTwoFactorCommandValidator : AbstractValidator<ReauthenticateTwoFactorCommand>
{
    /// <summary>Initializes the centralized second-factor request rules.</summary>
    public ReauthenticateTwoFactorCommandValidator()
    {
        RuleFor(request => request.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.AccessTokenId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
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
        RuleFor(request => request.Purpose)
            .Must(purpose => purpose is TwoFactorFlowPurpose.ReplaceAuthenticator or TwoFactorFlowPurpose.RegenerateRecoveryCodes)
            .WithMessage(ValidationMessages.InvalidTwoFactorManagementPurpose);
        RuleFor(request => request.Code)
            .Must((
                request,
                code) => code is not null || request.RecoveryCode is not null)
            .WithMessage(ValidationMessages.TwoFactorProofIsRequired);
        RuleFor(request => request.RecoveryCode)
            .Must((
                request,
                code) => code is null || request.Purpose == TwoFactorFlowPurpose.ReplaceAuthenticator)
            .WithMessage(ValidationMessages.TwoFactorRecoveryRequiresReplacement);
    }
}
