using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates metadata reads while allowing an absent identifier to select the latest request.</summary>
public class GetPersonalDataExportQueryValidator : AbstractValidator<GetPersonalDataExportQuery>
{
    /// <summary>Initializes the centralized validation rules.</summary>
    public GetPersonalDataExportQueryValidator()
    {
        RuleFor(request => request.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.ExportId)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
