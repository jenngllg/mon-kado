using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates administrative export identifiers and the mandatory external reference in the shared pipeline.</summary>
public class RequestAdministrativeDataExportCommandValidator : AbstractValidator<RequestAdministrativeDataExportCommand>
{
    /// <summary>Initializes the centralized validation rules.</summary>
    public RequestAdministrativeDataExportCommandValidator()
    {
        RuleFor(request => request.AdministratorId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.MemberId)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.RequestReference)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(reference => reference is null || (reference
                .Trim()
                .Length <= AdministrativeDataExportConstraints.MaximumReferenceLength && !reference.Any(char.IsControl)))
            .WithMessage(ValidationMessages.InvalidAdministrativeExportReference);
    }
}
