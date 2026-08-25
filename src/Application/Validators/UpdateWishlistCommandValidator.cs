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
            .WithMessage($"The wishlist name must be a single line of at most {WishlistTextValidation.MaximumNameLength} characters.");
        RuleFor(command => command.Occasion)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .IsInEnum()
            .WithMessage("The wishlist occasion is invalid.");
        RuleFor(command => command.Message)
            .Must(WishlistTextValidation.IsValidMessage)
            .WithMessage($"The wishlist message must not exceed {WishlistTextValidation.MaximumMessageLength} characters or contain unsupported control characters.");
    }
}
