using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates anonymous shared-wishlist report commands.
/// </summary>
public class ReportSharedWishlistCommandValidator : AbstractValidator<ReportSharedWishlistCommand>
{
    /// <summary>
    /// Initializes the shared-wishlist report validator.
    /// </summary>
    public ReportSharedWishlistCommandValidator()
    {
        RuleFor(command => command.ShareLinkId)
            .NotEmpty();
        RuleFor(command => command.Secret)
            .NotEmpty();
        RuleFor(command => command.Reason)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .IsInEnum()
            .WithMessage(ValidationMessages.InvalidWishlistReportReason);
        RuleFor(command => command.Details)
            .Must(WishlistReportTextValidation.IsValidDetails)
            .WithMessage(ValidationMessages.InvalidWishlistReportDetails);
        RuleFor(command => command.Details)
            .Must(details => !string.IsNullOrWhiteSpace(details))
            .When(command => command.Reason is WishlistReportReason.Other)
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
