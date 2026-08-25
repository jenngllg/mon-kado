using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates private wishlist deletion commands.
/// </summary>
public class DeleteWishlistCommandValidator : AbstractValidator<DeleteWishlistCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteWishlistCommandValidator" /> class.
    /// </summary>
    public DeleteWishlistCommandValidator()
    {
        RuleFor(command => command.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
