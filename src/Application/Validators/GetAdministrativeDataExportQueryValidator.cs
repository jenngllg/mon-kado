using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates administrative export identifiers in the shared pipeline.</summary>
public class GetAdministrativeDataExportQueryValidator : AbstractValidator<GetAdministrativeDataExportQuery>
{
    /// <summary>Initializes the centralized validation rules.</summary>
    public GetAdministrativeDataExportQueryValidator()
    {
        RuleFor(request => request.AdministratorId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.MemberId)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.ExportId)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
