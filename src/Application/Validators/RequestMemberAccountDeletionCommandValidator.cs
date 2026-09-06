using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates the account deletion request.</summary>
public class RequestMemberAccountDeletionCommandValidator : AbstractValidator<RequestMemberAccountDeletionCommand>
{
    /// <summary>Initializes the centralized validation rules.</summary>
    public RequestMemberAccountDeletionCommandValidator()
    {
        RuleFor(command => command.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
