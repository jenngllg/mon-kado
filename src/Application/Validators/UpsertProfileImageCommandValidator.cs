using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates profile-photo upload requests in the common pipeline.</summary>
public class UpsertProfileImageCommandValidator : AbstractValidator<UpsertProfileImageCommand>
{
    /// <summary>Initializes the profile-photo validation rules.</summary>
    public UpsertProfileImageCommandValidator()
    {
        RuleFor(command => command.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(command => command.Image)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProfileImage)
            .Must(image => image is null || image.Length <= GiftImageConstraints.MaximumInputLength)
            .WithMessage(ValidationMessages.ProfileImageTooLarge);
        RuleFor(command => command.HasValidMultipartShape)
            .Equal(true)
            .OverridePropertyName("Image")
            .WithMessage(ValidationMessages.MandatoryProfileImage);
    }
}
