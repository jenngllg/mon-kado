using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates the public member identifier in the shared pipeline.</summary>
public class GetPublicMemberProfileQueryValidator : AbstractValidator<GetPublicMemberProfileQuery>
{
    /// <summary>Initializes the member identifier rule.</summary>
    public GetPublicMemberProfileQueryValidator()
    {
        RuleFor(query => query.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
