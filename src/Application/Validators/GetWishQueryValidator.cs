using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates private gift wish retrieval queries.
/// </summary>
public class GetWishQueryValidator : AbstractValidator<GetWishQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetWishQueryValidator" /> class.
    /// </summary>
    public GetWishQueryValidator()
    {
        RuleFor(query => query.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.WishId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
