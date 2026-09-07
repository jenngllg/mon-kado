using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates report review input before application side effects.</summary>
public class GetWishlistReportQueryValidator : AbstractValidator<GetWishlistReportQuery>
{
    /// <summary>Configures centralized report review validation.</summary>
    public GetWishlistReportQueryValidator()
    {
        RuleFor(request => request.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.ReportId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
