using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates Google authentication completion commands.
/// </summary>
public class CompleteGoogleAuthenticationCommandValidator
    : AbstractValidator<CompleteGoogleAuthenticationCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompleteGoogleAuthenticationCommandValidator" /> class.
    /// </summary>
    public CompleteGoogleAuthenticationCommandValidator()
    {
        RuleFor(command => command.Identity)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty);
        When(
            command => command.Identity is not null,
            () => RuleFor(command => command.Identity)
                .SetValidator(new GoogleIdentityValidator()));
        RuleFor(command => command.ReturnPath)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .MaximumLength(GoogleReturnPathValidation.MaximumLength)
            .WithMessage("The return path must not exceed 256 characters.")
            .Must(GoogleReturnPathValidation.IsCanonical)
            .WithMessage("The return path is invalid.");
        RuleFor(command => command.FlowId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.ExpectedMemberId)
            .Must(memberId => memberId is not { } value || value != Guid.Empty)
            .WithMessage("The expected member identifier is invalid.");
        RuleFor(command => command.CurrentSessionId)
            .Must(sessionId => sessionId is not { } value || value != Guid.Empty)
            .WithMessage("The current session identifier is invalid.");
    }
}
