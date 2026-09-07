using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests read-only administrative report data.</summary>
/// <param name="wishlistId">The wishlistId.</param>
/// <param name="reason">The reason.</param>
/// <param name="page">The page.</param>
/// <param name="pageSize">The pageSize.</param>
public class GetWishlistReportsQuery(
    Guid wishlistId,
    WishlistReportReason? reason,
    int? page,
    int? pageSize) : IRequest<WishlistReportPage>
{
    /// <summary>Gets the wishlistId.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the reason.</summary>
    public WishlistReportReason? Reason { get; } = reason;
    /// <summary>Gets the page.</summary>
    public int? Page { get; } = page;
    /// <summary>Gets the pageSize.</summary>
    public int? PageSize { get; } = pageSize;
}

/// <summary>Coordinates validated administrative reads without logging private content.</summary>
/// <param name="service">The read service.</param>
/// <param name="logger">The structured logger.</param>
public class GetWishlistReportsQueryHandler(
    IReportedWishlistService service,
    ILogger<GetWishlistReportsQueryHandler> logger) : IRequestHandler<GetWishlistReportsQuery, WishlistReportPage>
{
    /// <summary>Retrieves the requested administrative result.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read-only result.</returns>
    public async Task<WishlistReportPage> Handle(
        GetWishlistReportsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.GetReportsAsync(
            request.WishlistId,
            request.Reason,
            request.Page ?? ReportedWishlistValidation.DefaultPage,
            request.PageSize ?? ReportedWishlistValidation.DefaultPageSize,
            cancellationToken);
        ReportedWishlistLogMessages.WishlistReportsRetrieved(
            logger,
            request.WishlistId);

        return result;
    }
}
