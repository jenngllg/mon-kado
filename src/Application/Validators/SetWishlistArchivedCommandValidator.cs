using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates the archive-state command in the common pipeline.</summary>
public class SetWishlistArchivedCommandValidator : AbstractValidator<SetWishlistArchivedCommand>
{
    /// <summary>Initializes the required identifiers and state rules.</summary>
    public SetWishlistArchivedCommandValidator()
    {
        RuleFor(command => command.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.IsArchived)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
