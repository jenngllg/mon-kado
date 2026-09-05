using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates current-member reservation history queries.
/// </summary>
public class GetGiftReservationHistoryQueryValidator : AbstractValidator<GetGiftReservationHistoryQuery>
{
    private static readonly string[] _allowedStatuses =
    [
        "active",
        "cancelled",
        "unavailable"
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="GetGiftReservationHistoryQueryValidator" /> class.
    /// </summary>
    public GetGiftReservationHistoryQueryValidator()
    {
        RuleFor(query => query.MemberId)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty);
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(GetGiftReservationHistoryQuery.DefaultPage)
            .When(query => query.Page.HasValue)
            .WithMessage(ValidationMessages.InvalidPage);
        RuleFor(query => query.PageSize)
            .InclusiveBetween(
                GetGiftReservationHistoryQuery.DefaultPage,
                GetGiftReservationHistoryQuery.MaximumPageSize)
            .When(query => query.PageSize.HasValue)
            .WithMessage(ValidationMessages.InvalidPageSize);
        RuleFor(query => query.Status)
            .Must(status => _allowedStatuses.Contains(
                status,
                StringComparer.Ordinal))
            .When(query => query.Status is not null)
            .WithMessage(ValidationMessages.InvalidGiftReservationHistoryStatus);
    }
}
