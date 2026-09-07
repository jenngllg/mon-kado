using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates a browser submission before any protected cookie is consumed.</summary>
public class CompleteGoogleSessionCommandValidator : AbstractValidator<CompleteGoogleSessionCommand>
{
    /// <summary>Initializes the Google completion submission rules.</summary>
    public CompleteGoogleSessionCommandValidator()
    {
        RuleFor(request => request.Flow)
            .Must(GoogleFlowValidation.IsCanonical)
            .WithMessage(ValidationMessages.InvalidGoogleFlow);
    }
}
