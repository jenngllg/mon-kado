using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates profile-photo deletion requests in the common pipeline.</summary>
public class DeleteProfileImageCommandValidator : AbstractValidator<DeleteProfileImageCommand>
{
    /// <summary>Initializes the profile-photo deletion rules.</summary>
    public DeleteProfileImageCommandValidator()
    {
        RuleFor(command => command.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
