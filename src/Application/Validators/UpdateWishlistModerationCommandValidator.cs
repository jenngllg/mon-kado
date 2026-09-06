using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates administrator suspension decisions before any application side effects.</summary>
public class UpdateWishlistModerationCommandValidator : AbstractValidator<UpdateWishlistModerationCommand>
{
    /// <summary>Configures mandatory state and conditional private reason rules.</summary>
    public UpdateWishlistModerationCommandValidator()
    {
        RuleFor(command => command.AdministratorId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.IsSuspended)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.Reason)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(IsValidReason)
            .WithMessage("The suspension reason must contain valid text of at most 1000 characters.")
            .When(command => command.IsSuspended is true);
        RuleFor(command => command.Reason)
            .Null()
            .WithMessage("The reason must be null when reactivating a wishlist.")
            .When(command => command.IsSuspended is false);
    }

    /// <summary>Checks normalized Unicode length and rejects malformed or hidden control data.</summary>
    /// <param name="value">The candidate private reason.</param>
    /// <returns>Whether the text can safely be normalized and persisted.</returns>
    private static bool IsValidReason(string? value)
    {
        try
        {
            var normalized = (value ?? string.Empty)
                .Trim()
                .Normalize(NormalizationForm.FormC);

            return normalized
                .EnumerateRunes()
                .Count() <= WishlistModerationValidation.MaximumReasonLength && normalized
                .EnumerateRunes()
                .All(rune => !Rune.IsControl(rune) || rune.Value is '\r' or '\n' or '\t');
        }
        catch (ArgumentException)
        {

            return false;
        }
    }
}
