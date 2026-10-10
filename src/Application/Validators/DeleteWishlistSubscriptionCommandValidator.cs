using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates subscription inputs in the common pipeline.</summary>
public class DeleteWishlistSubscriptionCommandValidator : AbstractValidator<DeleteWishlistSubscriptionCommand>
{
    /// <summary>Initializes the validator.</summary>
    public DeleteWishlistSubscriptionCommandValidator()
    {
        RuleFor(request => request.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(request => request.Id)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
