using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates gift-reservation cancellation commands.
/// </summary>
public class CancelGiftReservationCommandValidator : AbstractValidator<CancelGiftReservationCommand>
{
    /// <summary>Initializes a gift-reservation cancellation validator.</summary>
    public CancelGiftReservationCommandValidator()
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
    }
}
