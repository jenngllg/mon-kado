using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates gift image deletion commands.
/// </summary>
public class DeleteWishImageCommandValidator : AbstractValidator<DeleteWishImageCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteWishImageCommandValidator" /> class.
    /// </summary>
    public DeleteWishImageCommandValidator()
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
