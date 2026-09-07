using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates report identities, filters and one-based pagination.</summary>
public class GetReportedWishImageQueryValidator : AbstractValidator<GetReportedWishImageQuery>
{
    /// <summary>Configures centralized request validation.</summary>
    public GetReportedWishImageQueryValidator()
    {
        RuleFor(query => query.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.WishId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
