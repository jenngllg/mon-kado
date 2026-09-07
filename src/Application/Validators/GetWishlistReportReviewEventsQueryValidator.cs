using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates report review input before application side effects.</summary>
public class GetWishlistReportReviewEventsQueryValidator : AbstractValidator<GetWishlistReportReviewEventsQuery>
{
    /// <summary>Configures centralized report review validation.</summary>
    public GetWishlistReportReviewEventsQueryValidator()
    {
        RuleFor(request => request.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.ReportId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(ReportedWishlistValidation.DefaultPage)
            .When(request => request.Page.HasValue)
            .WithMessage(ValidationMessages.InvalidPage);
        RuleFor(request => request.PageSize)
            .InclusiveBetween(
            ReportedWishlistValidation.DefaultPage,
            ReportedWishlistValidation.MaximumPageSize)
            .When(request => request.PageSize.HasValue)
            .WithMessage(ValidationMessages.InvalidPageSize);
    }
}
