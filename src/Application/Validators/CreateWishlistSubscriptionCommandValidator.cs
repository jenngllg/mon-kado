using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates subscription inputs in the common pipeline.</summary>
public class CreateWishlistSubscriptionCommandValidator : AbstractValidator<CreateWishlistSubscriptionCommand>
{
    /// <summary>Initializes the validator.</summary>
    public CreateWishlistSubscriptionCommandValidator()
    {
        RuleFor(request => request.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.ShareLinkId)
            .NotEmpty();
        RuleFor(request => request.Secret)
            .NotEmpty()
            .MaximumLength(512);
    }
}
