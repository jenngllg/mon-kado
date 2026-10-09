using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates private-owner reservation identifiers.</summary>
public class GetOwnedGiftReservationQueryValidator : AbstractValidator<GetOwnedGiftReservationQuery>
{
    /// <summary>Initializes centralized input rules.</summary>
    public GetOwnedGiftReservationQueryValidator()
    {
        RuleFor(request => request.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.WishId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
