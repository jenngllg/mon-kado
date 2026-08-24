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
            .WithMessage($"The wishlist name must be a single line of at most {WishlistTextValidation.MaximumNameLength} characters.");
        RuleFor(command => command.Occasion)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .IsInEnum()
            .WithMessage("The wishlist occasion is invalid.");
        RuleFor(command => command.EventDate)
            .Must(BeTodayOrLater)
            .WithMessage("The event date must be today or later.");
        RuleFor(command => command.Message)
            .Must(WishlistTextValidation.IsValidMessage)
            .WithMessage($"The wishlist message must not exceed {WishlistTextValidation.MaximumMessageLength} characters or contain unsupported control characters.");
    }

    private bool BeTodayOrLater(DateOnly? eventDate)
    {
        if (eventDate is null)
            return true;

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        return eventDate.Value >= today;
    }
}
