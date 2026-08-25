using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates manual gift wish creation commands.
/// </summary>
public class CreateWishCommandValidator : AbstractValidator<CreateWishCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateWishCommandValidator" /> class.
    /// </summary>
    public CreateWishCommandValidator()
    {
        RuleFor(command => command.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(WishTextValidation.IsValidName)
            .WithMessage(ValidationMessages.InvalidWishName);
        RuleFor(command => command.Note)
            .Must(WishTextValidation.IsValidNote)
            .WithMessage(ValidationMessages.InvalidWishNote);
        RuleFor(command => command.Url)
            .Must(WishTextValidation.IsValidUrl)
            .WithMessage(ValidationMessages.InvalidWishUrl);
        RuleFor(command => command.Price)
            .GreaterThan(0)
            .WithMessage(ValidationMessages.InvalidWishPrice)
            .PrecisionScale(
                WishTextValidation.MaximumPricePrecision,
                WishTextValidation.MaximumPriceScale,
                ignoreTrailingZeros: false)
            .WithMessage(ValidationMessages.InvalidWishPrice);
    }
}
