using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates wishlist share-link deletion commands.</summary>
public class DeleteWishlistShareLinkCommandValidator : AbstractValidator<DeleteWishlistShareLinkCommand>
{
    /// <summary>Initializes the validator.</summary>
    public DeleteWishlistShareLinkCommandValidator()
    {
        RuleFor(command => command.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
