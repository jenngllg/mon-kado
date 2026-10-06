using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates preference-only mutations in the common pipeline.</summary>
public class SetWishFavoriteCommandValidator : AbstractValidator<SetWishFavoriteCommand>
{
    /// <summary>Initializes required identifiers and preference rules.</summary>
    public SetWishFavoriteCommandValidator()
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
        RuleFor(command => command.IsFavorite)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
