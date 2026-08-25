using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates complete gift wish collection queries.
/// </summary>
public class GetWishesQueryValidator : AbstractValidator<GetWishesQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetWishesQueryValidator" /> class.
    /// </summary>
    public GetWishesQueryValidator()
    {
        RuleFor(query => query.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
