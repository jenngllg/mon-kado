using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates gift wish deletion commands.
/// </summary>
public class DeleteWishCommandValidator : AbstractValidator<DeleteWishCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteWishCommandValidator" /> class.
    /// </summary>
    public DeleteWishCommandValidator()
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
    }
}
