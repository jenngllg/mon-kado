using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Aggregates invalid browser bindings and current password inputs before authentication.</summary>
public class LinkGoogleSessionCommandValidator : AbstractValidator<LinkGoogleSessionCommand>
{
    /// <summary>Initializes the Google association submission rules.</summary>
    public LinkGoogleSessionCommandValidator()
    {
        RuleFor(request => request.Flow)
            .Must(GoogleFlowValidation.IsCanonical)
            .WithMessage(ValidationMessages.InvalidGoogleFlow);
        RuleFor(request => request.CurrentPassword)
            .ApplySubmittedPasswordRules();
    }
}
