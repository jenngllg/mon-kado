using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates owned wishlist collection queries.
/// </summary>
public class GetWishlistsQueryValidator : AbstractValidator<GetWishlistsQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetWishlistsQueryValidator" /> class.
    /// </summary>
    public GetWishlistsQueryValidator()
    {
        RuleFor(query => query.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
