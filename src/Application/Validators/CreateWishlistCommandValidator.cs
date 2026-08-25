using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates private wishlist creation commands.
/// </summary>
public class CreateWishlistCommandValidator : AbstractValidator<CreateWishlistCommand>
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateWishlistCommandValidator" /> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    public CreateWishlistCommandValidator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        RuleFor(command => command.OwnerId)
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
        RuleFor(command => command.EventDate)
            .Must(BeTodayOrLater)
            .WithMessage(ValidationMessages.WishlistEventDateMustBeTodayOrLater);
        RuleFor(command => command.Message)
            .Must(WishlistTextValidation.IsValidMessage)
            .WithMessage(ValidationMessages.InvalidWishlistMessage);
    }

    private bool BeTodayOrLater(DateOnly? eventDate)
    {
        if (eventDate is null)
            return true;

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        return eventDate.Value >= today;
    }
}
