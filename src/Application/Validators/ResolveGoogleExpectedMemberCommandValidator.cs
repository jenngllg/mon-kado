using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates Google expected-member resolution commands.
/// </summary>
public class ResolveGoogleExpectedMemberCommandValidator
    : AbstractValidator<ResolveGoogleExpectedMemberCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResolveGoogleExpectedMemberCommandValidator" /> class.
    /// </summary>
    public ResolveGoogleExpectedMemberCommandValidator()
    {
        RuleFor(command => command.Identity)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty);
        When(
            command => command.Identity is not null,
            () => RuleFor(command => command.Identity)
                .SetValidator(new GoogleIdentityValidator()));
    }
}
