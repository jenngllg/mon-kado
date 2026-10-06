using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates public-preview identifiers in the common pipeline.</summary>
public class GetWishlistSharePreviewQueryValidator : AbstractValidator<GetWishlistSharePreviewQuery>
{
    /// <summary>Initializes the share-link identifier rule.</summary>
    public GetWishlistSharePreviewQueryValidator()
    {
        RuleFor(query => query.ShareLinkId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
