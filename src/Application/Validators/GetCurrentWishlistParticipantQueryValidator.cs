using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates current-participant queries.
/// </summary>
public class GetCurrentWishlistParticipantQueryValidator : AbstractValidator<GetCurrentWishlistParticipantQuery>
{
    /// <summary>Initializes the current-participant query validator.</summary>
    public GetCurrentWishlistParticipantQueryValidator()
    {
        RuleFor(query => query.ShareLinkId)
            .NotEmpty();
        RuleFor(query => query.Secret)
            .NotEmpty();
        RuleFor(query => query.MemberId)
            .Must(memberId => memberId != Guid.Empty);
    }
}
