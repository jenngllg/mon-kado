using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates shared-wish copy identifiers without trusting browser metadata.</summary>
public class CopyWishCommandValidator : AbstractValidator<CopyWishCommand>
{
    /// <summary>Initializes the centralized copy validation rules.</summary>
    public CopyWishCommandValidator()
    {
        RuleFor(command => command.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.SourceShareLinkId)
            .NotEmpty()
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.SourceWishId)
            .NotEmpty()
            .NotEqual(Guid.Empty)
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
