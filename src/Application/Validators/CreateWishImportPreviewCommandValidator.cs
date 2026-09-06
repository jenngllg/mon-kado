using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates merchant preview requests in the common pipeline.</summary>
public class CreateWishImportPreviewCommandValidator : AbstractValidator<CreateWishImportPreviewCommand>
{
    /// <summary>Defines presence and safe URL syntax rules.</summary>
    public CreateWishImportPreviewCommandValidator()
    {
        RuleFor(request => request.OwnerId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.WishlistId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.Url)
            .Must(WishImportUrlValidation.IsValid)
            .WithMessage(ValidationMessages.InvalidWishImportUrl);
    }
}
