using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates private wishlist update commands.
/// </summary>
public class UpdateWishlistCommandValidator : AbstractValidator<UpdateWishlistCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateWishlistCommandValidator" /> class.
    /// </summary>
    public UpdateWishlistCommandValidator()
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
            .Must(WishlistTextValidation.IsValidName)
            .WithMessage(ValidationMessages.InvalidWishlistName);
        RuleFor(command => command.Occasion)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .IsInEnum()
            .WithMessage(ValidationMessages.InvalidWishlistOccasion);
        RuleFor(command => command.Message)
            .Must(WishlistTextValidation.IsValidMessage)
            .WithMessage(ValidationMessages.InvalidWishlistMessage);
    }
}
