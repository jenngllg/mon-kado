using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests a private administrative report review operation.</summary>
/// <param name="wishlistId">The wishlistId.</param>
/// <param name="reportId">The reportId.</param>
/// <param name="page">The page.</param>
/// <param name="pageSize">The pageSize.</param>
public class GetWishlistReportReviewEventsQuery(
    Guid wishlistId,
    Guid reportId,
    int? page,
    int? pageSize) : IRequest<WishlistReportReviewEventPage>
{
    /// <summary>Gets the wishlistId.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the reportId.</summary>
    public Guid ReportId { get; } = reportId;
    /// <summary>Gets the page.</summary>
    public int? Page { get; } = page;
    /// <summary>Gets the pageSize.</summary>
    public int? PageSize { get; } = pageSize;
}

/// <summary>Coordinates a validated report review operation without logging private notes.</summary>
/// <param name="service">The report review service.</param>
/// <param name="logger">The structured logger.</param>
public class GetWishlistReportReviewEventsQueryHandler(
    IWishlistReportReviewService service,
    ILogger<GetWishlistReportReviewEventsQueryHandler> logger) : IRequestHandler<GetWishlistReportReviewEventsQuery, WishlistReportReviewEventPage>
{
    /// <summary>Executes the validated report review request.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The safe administrative result.</returns>
    public async Task<WishlistReportReviewEventPage> Handle(
        GetWishlistReportReviewEventsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.GetEventsAsync(
            request.WishlistId,
            request.ReportId,
            request.Page ?? ReportedWishlistValidation.DefaultPage,
            request.PageSize ?? ReportedWishlistValidation.DefaultPageSize,
            cancellationToken);
        WishlistReportReviewLogMessages.WishlistReportReviewEventsRetrieved(
            logger,
            request.WishlistId,
            request.ReportId);

        return result;
    }
}
