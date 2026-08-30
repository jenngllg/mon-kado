using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates gift-reservation creation and replacement commands.
/// </summary>
public class UpsertGiftReservationCommandValidator : AbstractValidator<UpsertGiftReservationCommand>
{
    /// <summary>Initializes a gift-reservation command validator.</summary>
    public UpsertGiftReservationCommandValidator()
    {
        RuleFor(command => command.ShareLinkId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.Secret)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.MemberId)
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.Quantity)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .InclusiveBetween(
                WishTextValidation.MinimumQuantity,
                WishTextValidation.MaximumQuantity)
            .WithMessage(ValidationMessages.InvalidGiftQuantity);
    }
}
