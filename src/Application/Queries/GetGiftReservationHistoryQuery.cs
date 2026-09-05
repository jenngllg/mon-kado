using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>
/// Represents a request for one page of the current member's reservation history.
/// </summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="page">The optional one-based page number.</param>
/// <param name="pageSize">The optional page size.</param>
/// <param name="status">The optional lifecycle status filter.</param>
public class GetGiftReservationHistoryQuery(
    Guid memberId,
    int? page,
    int? pageSize,
    string? status) : IRequest<GiftReservationHistoryPage>, IGenericValidationFailure
{
    /// <summary>Gets the default one-based page number.</summary>
    public const int DefaultPage = 1;

    /// <summary>Gets the default page size.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Gets the maximum page size.</summary>
    public const int MaximumPageSize = 100;

    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>Gets the optional one-based page number.</summary>
    public int? Page { get; } = page;

    /// <summary>Gets the optional page size.</summary>
    public int? PageSize { get; } = pageSize;

    /// <summary>Gets the optional lifecycle status filter.</summary>
    public string? Status { get; } = status;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles current-member reservation history queries.
/// </summary>
/// <param name="historyService">The reservation history service.</param>
/// <param name="logger">The logger.</param>
public class GetGiftReservationHistoryQueryHandler(
    IGiftReservationHistoryService historyService,
    ILogger<GetGiftReservationHistoryQueryHandler> logger)
    : IRequestHandler<GetGiftReservationHistoryQuery, GiftReservationHistoryPage>
{
    /// <summary>
    /// Gets one page of the current member's reservation history.
    /// </summary>
    /// <param name="request">The history query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested history page.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    public async Task<GiftReservationHistoryPage> Handle(
        GetGiftReservationHistoryQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.GiftReservationHistoryRetrievalStarted(
            logger,
            request.MemberId);
        var page = request.Page ?? GetGiftReservationHistoryQuery.DefaultPage;
        var pageSize = request.PageSize ?? GetGiftReservationHistoryQuery.DefaultPageSize;
        var status = ParseStatus(request.Status);
        var history = await historyService.GetAsync(
            request.MemberId,
            page,
            pageSize,
            status,
            cancellationToken);

        if (history is null)
            throw new InvalidAuthenticationSessionException();

        ApplicationLogMessages.GiftReservationHistoryRetrieved(
            logger,
            request.MemberId,
            history.TotalCount);

        return history;
    }

    private static GiftReservationHistoryStatus? ParseStatus(string? status)
    {
        return status switch
        {
            "active" => GiftReservationHistoryStatus.Active,
            "cancelled" => GiftReservationHistoryStatus.Cancelled,
            "unavailable" => GiftReservationHistoryStatus.Unavailable,
            _ => null
        };
    }
}
