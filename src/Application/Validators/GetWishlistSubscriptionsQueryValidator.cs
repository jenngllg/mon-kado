using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates subscription inputs in the common pipeline.</summary>
public class GetWishlistSubscriptionsQueryValidator : AbstractValidator<GetWishlistSubscriptionsQuery>
{
    /// <summary>Initializes the validator.</summary>
    public GetWishlistSubscriptionsQueryValidator()
    {
        RuleFor(request => request.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(GetWishlistSubscriptionsQuery.DefaultPage)
            .When(request => request.Page.HasValue)
            .WithMessage(ValidationMessages.InvalidPage);
        RuleFor(request => request.PageSize)
            .InclusiveBetween(
                GetWishlistSubscriptionsQuery.DefaultPage,
                GetWishlistSubscriptionsQuery.MaximumPageSize)
            .When(request => request.PageSize.HasValue)
            .WithMessage(ValidationMessages.InvalidPageSize);
    }
}
