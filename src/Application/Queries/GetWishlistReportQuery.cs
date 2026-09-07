using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests a private administrative report review operation.</summary>
/// <param name="wishlistId">The wishlistId.</param>
/// <param name="reportId">The reportId.</param>
public class GetWishlistReportQuery(
    Guid wishlistId,
    Guid reportId) : IRequest<VersionedWishlistReportDetails>
{
    /// <summary>Gets the wishlistId.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the reportId.</summary>
    public Guid ReportId { get; } = reportId;
}

/// <summary>Coordinates a validated report review operation without logging private notes.</summary>
/// <param name="service">The report review service.</param>
/// <param name="logger">The structured logger.</param>
public class GetWishlistReportQueryHandler(
    IWishlistReportReviewService service,
    ILogger<GetWishlistReportQueryHandler> logger) : IRequestHandler<GetWishlistReportQuery, VersionedWishlistReportDetails>
{
    /// <summary>Executes the validated report review request.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The safe administrative result.</returns>
    public async Task<VersionedWishlistReportDetails> Handle(
        GetWishlistReportQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(
            request.WishlistId,
            request.ReportId,
            cancellationToken);
        WishlistReportReviewLogMessages.WishlistReportRetrieved(
            logger,
            request.WishlistId,
            request.ReportId);

        return result;
    }
}
