using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates private wishlist retrieval queries.
/// </summary>
public class GetWishlistQueryValidator : AbstractValidator<GetWishlistQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetWishlistQueryValidator" /> class.
    /// </summary>
    public GetWishlistQueryValidator()
    {
        RuleFor(query => query.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
