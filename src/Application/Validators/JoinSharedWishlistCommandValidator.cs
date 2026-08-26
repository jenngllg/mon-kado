using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates shared-wishlist join commands.
/// </summary>
public class JoinSharedWishlistCommandValidator : AbstractValidator<JoinSharedWishlistCommand>
{
    /// <summary>Initializes the shared-wishlist join validator.</summary>
    public JoinSharedWishlistCommandValidator()
    {
        RuleFor(command => command.ShareLinkId)
            .NotEmpty();
        RuleFor(command => command.Secret)
            .NotEmpty();
        RuleFor(command => command.MemberId)
            .Must(memberId => memberId != Guid.Empty);
        RuleFor(command => command.DisplayName)
            .ApplyDisplayNameRules()
            .When(command => command.MemberId is null);
    }
}
