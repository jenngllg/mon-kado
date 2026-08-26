using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates public shared-wishlist queries.</summary>
public class GetSharedWishlistQueryValidator : AbstractValidator<GetSharedWishlistQuery>
{
    /// <summary>Initializes the validator.</summary>
    public GetSharedWishlistQueryValidator()
    {
        RuleFor(query => query.ShareLinkId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.Secret)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
