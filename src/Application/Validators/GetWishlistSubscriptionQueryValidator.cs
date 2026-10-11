using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates subscription inputs in the common pipeline.</summary>
public class GetWishlistSubscriptionQueryValidator : AbstractValidator<GetWishlistSubscriptionQuery>
{
    /// <summary>Initializes the validator.</summary>
    public GetWishlistSubscriptionQueryValidator()
    {
        RuleFor(request => request.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.Id)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
