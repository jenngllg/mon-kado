using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates owner wishlist share-link retrieval queries.</summary>
public class GetWishlistShareLinkQueryValidator : AbstractValidator<GetWishlistShareLinkQuery>
{
    /// <summary>Initializes the validator.</summary>
    public GetWishlistShareLinkQueryValidator()
    {
        RuleFor(query => query.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
