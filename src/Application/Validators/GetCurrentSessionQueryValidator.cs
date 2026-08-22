using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates current authenticated member session queries.
/// </summary>
public class GetCurrentSessionQueryValidator : AbstractValidator<GetCurrentSessionQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetCurrentSessionQueryValidator" /> class.
    /// </summary>
    public GetCurrentSessionQueryValidator()
    {
        RuleFor(query => query.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
    }
}
