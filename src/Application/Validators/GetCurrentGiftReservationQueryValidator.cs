using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates current gift-reservation queries.
/// </summary>
public class GetCurrentGiftReservationQueryValidator : AbstractValidator<GetCurrentGiftReservationQuery>
{
    /// <summary>Initializes a current gift-reservation query validator.</summary>
    public GetCurrentGiftReservationQueryValidator()
    {
        RuleFor(query => query.ShareLinkId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.Secret)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.WishId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.MemberId)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
