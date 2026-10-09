using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates private-owner reservation identifiers.</summary>
public class CancelOwnedGiftReservationCommandValidator : AbstractValidator<CancelOwnedGiftReservationCommand>
{
    /// <summary>Initializes centralized input rules.</summary>
    public CancelOwnedGiftReservationCommandValidator()
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
