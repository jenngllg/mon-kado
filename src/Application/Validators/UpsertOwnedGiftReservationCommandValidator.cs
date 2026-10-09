using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates private-owner reservation identifiers and quantity.</summary>
public class UpsertOwnedGiftReservationCommandValidator : AbstractValidator<UpsertOwnedGiftReservationCommand>
{
    /// <summary>Initializes centralized input rules.</summary>
    public UpsertOwnedGiftReservationCommandValidator()
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
        RuleFor(request => request.Quantity)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .InclusiveBetween(
                WishTextValidation.MinimumQuantity,
                WishTextValidation.MaximumQuantity)
            .WithMessage(ValidationMessages.InvalidGiftQuantity);
    }
}

