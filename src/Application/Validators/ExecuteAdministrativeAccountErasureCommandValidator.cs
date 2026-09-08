using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates target confirmation and the technical reference in the shared pipeline.</summary>
public class ExecuteAdministrativeAccountErasureCommandValidator : AbstractValidator<ExecuteAdministrativeAccountErasureCommand>
{
    /// <summary>Initializes the complete input validation rules.</summary>
    public ExecuteAdministrativeAccountErasureCommandValidator()
    {
        RuleFor(request => request.AdministratorId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.MemberId)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.ConfirmedMemberId)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Equal(request => request.MemberId)
            .WithMessage(ValidationMessages.ErasureTargetMismatch);
        RuleFor(request => request.RequestReference)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(reference => reference is null || (reference
                .Trim()
                .Length <= AdministrativeAccountErasureConstraints.MaximumReferenceLength && !reference.Any(char.IsControl)))
            .WithMessage(ValidationMessages.InvalidAdministrativeExportReference);
    }
}
