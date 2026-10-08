using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates identifiers for copies between distinct owned lists.</summary>
public class CopyOwnedWishCommandValidator : AbstractValidator<CopyOwnedWishCommand>
{
    /// <summary>Initializes centralized owned-copy validation rules.</summary>
    public CopyOwnedWishCommandValidator()
    {
        RuleFor(command => command.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.SourceWishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.SourceWishId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.DestinationWishlistId)
            .NotEmpty()
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty)
            .NotEqual(command => command.SourceWishlistId)
            .WithMessage(ValidationMessages.DestinationWishlistMustDiffer);
    }
}
