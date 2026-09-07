using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates the authenticated export request identity.</summary>
public class RequestPersonalDataExportCommandValidator : AbstractValidator<RequestPersonalDataExportCommand>
{
    /// <summary>Initializes the centralized validation rules.</summary>
    public RequestPersonalDataExportCommandValidator()
    {
        RuleFor(request => request.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
