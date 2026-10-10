using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates subscription inputs in the common pipeline.</summary>
public class GetCurrentWishlistSubscriptionQueryValidator : AbstractValidator<GetCurrentWishlistSubscriptionQuery>
{
    /// <summary>Initializes the validator.</summary>
    public GetCurrentWishlistSubscriptionQueryValidator()
    {
        RuleFor(request => request.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.ShareLinkId)
            .NotEmpty();
        RuleFor(request => request.Secret)
            .NotEmpty()
            .MaximumLength(512);
    }
}
