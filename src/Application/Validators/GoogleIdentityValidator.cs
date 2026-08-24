using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates the limited identity claims accepted from the Google OIDC middleware.
/// </summary>
public class GoogleIdentityValidator : AbstractValidator<GoogleIdentity?>
{
    private const int MaximumHostedDomainLength = 253;

    /// <summary>
    /// Identifies the maximum Google subject length.
    /// </summary>
    public const int MaximumSubjectLength = 255;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleIdentityValidator" /> class.
    /// </summary>
    public GoogleIdentityValidator()
    {
        When(
            identity => identity is not null,
            () =>
            {
                RuleFor(identity => identity!.Subject)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty()
                    .OverridePropertyName(nameof(GoogleIdentity.Subject))
                    .WithMessage(ValidationMessages.MandatoryProperty)
                    .MaximumLength(MaximumSubjectLength)
                    .WithMessage("The Google subject must not exceed 255 characters.")
                    .Matches("^[\\x21-\\x7E]+$")
                    .WithMessage("The Google subject must contain printable ASCII characters only.");
                RuleFor(identity => identity!.Email)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty()
                    .OverridePropertyName(nameof(GoogleIdentity.Email))
                    .WithMessage(ValidationMessages.MandatoryProperty)
                    .Must(EmailAddressValidation.IsWithinMaximumLength)
                    .WithMessage(ValidationMessages.EmailAddressTooLong)
                    .Must(EmailAddressValidation.IsValid)
                    .WithMessage(ValidationMessages.InvalidEmailAddress);
                RuleFor(identity => identity!.EmailVerified)
                    .Equal(true)
                    .OverridePropertyName(nameof(GoogleIdentity.EmailVerified))
                    .WithMessage("The Google email address must be verified.");
                RuleFor(identity => identity!.HostedDomain)
                    .MaximumLength(MaximumHostedDomainLength)
                    .OverridePropertyName(nameof(GoogleIdentity.HostedDomain))
                    .WithMessage("The Google hosted domain must not exceed 253 characters.")
                    .When(identity => identity!.HostedDomain is not null);
                RuleFor(identity => identity!.DisplayName)
                    .ApplyDisplayNameRules()
                    .OverridePropertyName(nameof(GoogleIdentity.DisplayName))
                    .When(identity => identity!.DisplayName is not null);
            });
    }
}
