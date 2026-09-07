using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates the owned archive download identifiers.</summary>
public class DownloadPersonalDataExportQueryValidator : AbstractValidator<DownloadPersonalDataExportQuery>
{
    /// <summary>Initializes the centralized validation rules.</summary>
    public DownloadPersonalDataExportQueryValidator()
    {
        RuleFor(request => request.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.ExportId)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
