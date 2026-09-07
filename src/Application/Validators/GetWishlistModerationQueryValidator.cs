using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates the moderation-state query.</summary>
public class GetWishlistModerationQueryValidator : AbstractValidator<GetWishlistModerationQuery>
{
    /// <summary>Configures moderation-state input validation.</summary>
    public GetWishlistModerationQueryValidator()
    {
        RuleFor(query => query.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
