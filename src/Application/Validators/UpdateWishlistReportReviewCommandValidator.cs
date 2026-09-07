using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates report review input before application side effects.</summary>
public class UpdateWishlistReportReviewCommandValidator : AbstractValidator<UpdateWishlistReportReviewCommand>
{
    /// <summary>Configures centralized report review validation.</summary>
    public UpdateWishlistReportReviewCommandValidator()
    {
        RuleFor(request => request.AdministratorId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.ReportId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.Status)
            .NotNull()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .IsInEnum();
        RuleFor(request => request.ReviewNote)
            .Must(WishlistReportTextValidation.IsValidDetails)
            .WithMessage(ValidationMessages.InvalidReportReviewNote);
    }
}
