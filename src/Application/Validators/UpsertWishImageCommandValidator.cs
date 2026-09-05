using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates gift-wish image add and replacement commands.
/// </summary>
public class UpsertWishImageCommandValidator : AbstractValidator<UpsertWishImageCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpsertWishImageCommandValidator" /> class.
    /// </summary>
    public UpsertWishImageCommandValidator()
    {
        RuleFor(command => command.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.Image)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryGiftImage)
            .Must(image => image.Length <= GiftImageConstraints.MaximumInputLength)
            .WithMessage(ValidationMessages.GiftImageTooLarge);
        RuleFor(command => command.HasValidMultipartShape)
            .Equal(true)
            .WithName("Image")
            .WithMessage(ValidationMessages.MandatoryGiftImage);
    }
}
