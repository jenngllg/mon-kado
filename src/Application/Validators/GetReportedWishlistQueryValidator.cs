using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates report identities, filters and one-based pagination.</summary>
public class GetReportedWishlistQueryValidator : AbstractValidator<GetReportedWishlistQuery>
{
    /// <summary>Configures centralized request validation.</summary>
    public GetReportedWishlistQueryValidator()
    {
        RuleFor(query => query.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
