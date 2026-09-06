using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates public profile-photo identifiers before the shared query pipeline.</summary>
public class GetProfileImageQueryValidator : AbstractValidator<GetProfileImageQuery>
{
    /// <summary>Initializes public profile-photo identifier rules.</summary>
    public GetProfileImageQueryValidator()
    {
        RuleFor(query => query.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.ImageId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
