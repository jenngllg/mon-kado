using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates complete gift wish collection reorder commands.
/// </summary>
public class ReorderWishesCommandValidator : AbstractValidator<ReorderWishesCommand>
{
    /// <summary>
    /// Defines the maximum number of wishes accepted in one collection.
    /// </summary>
    public const int MaximumWishCount = 1000;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReorderWishesCommandValidator" /> class.
    /// </summary>
    public ReorderWishesCommandValidator()
    {
        RuleFor(command => command.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishIds)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(wishIds => wishIds is null || wishIds.Count <= MaximumWishCount)
            .WithMessage($"The property {{PropertyName}} must contain at most {MaximumWishCount} items.")
            .Must(wishIds => wishIds is null || wishIds.Distinct().Count() == wishIds.Count)
            .WithMessage("The property {PropertyName} must not contain duplicate values.");
        RuleForEach(command => command.WishIds)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
