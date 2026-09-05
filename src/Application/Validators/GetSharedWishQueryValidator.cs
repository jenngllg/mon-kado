using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates detailed public gift-wish queries.
/// </summary>
public class GetSharedWishQueryValidator : AbstractValidator<GetSharedWishQuery>
{
    /// <summary>Initializes the validator.</summary>
    public GetSharedWishQueryValidator()
    {
        RuleFor(query => query.ShareLinkId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.Secret)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.WishId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.MemberId)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
