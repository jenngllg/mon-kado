using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates moderation history pagination.</summary>
public class GetWishlistModerationEventsQueryValidator : AbstractValidator<GetWishlistModerationEventsQuery>
{
    /// <summary>Configures identity and one-based pagination rules.</summary>
    public GetWishlistModerationEventsQueryValidator()
    {
        RuleFor(query => query.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(WishlistModerationValidation.DefaultPage)
            .When(query => query.Page.HasValue)
            .WithMessage(ValidationMessages.InvalidPage);
        RuleFor(query => query.PageSize)
            .InclusiveBetween(
            WishlistModerationValidation.DefaultPage,
            WishlistModerationValidation.MaximumPageSize)
            .When(query => query.PageSize.HasValue)
            .WithMessage(ValidationMessages.InvalidPageSize);
    }
}
