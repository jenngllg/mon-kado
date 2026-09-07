using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates report identities, filters and one-based pagination.</summary>
public class GetWishlistReportsQueryValidator : AbstractValidator<GetWishlistReportsQuery>
{
    /// <summary>Configures centralized request validation.</summary>
    public GetWishlistReportsQueryValidator()
    {
        RuleFor(query => query.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.Reason)
            .IsInEnum();
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(ReportedWishlistValidation.DefaultPage)
            .When(query => query.Page.HasValue)
            .WithMessage(ValidationMessages.InvalidPage);
        RuleFor(query => query.PageSize)
            .InclusiveBetween(
            ReportedWishlistValidation.DefaultPage,
            ReportedWishlistValidation.MaximumPageSize)
            .When(query => query.PageSize.HasValue)
            .WithMessage(ValidationMessages.InvalidPageSize);
    }
}
