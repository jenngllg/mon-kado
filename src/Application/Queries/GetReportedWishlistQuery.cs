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
public class GetReportedWishlistQuery(Guid wishlistId) : IRequest<ReportedWishlistDetails>
{
    /// <summary>Gets the wishlistId.</summary>
    public Guid WishlistId { get; } = wishlistId;
}

/// <summary>Coordinates validated administrative reads without logging private content.</summary>
/// <param name="service">The read service.</param>
/// <param name="logger">The structured logger.</param>
public class GetReportedWishlistQueryHandler(
    IReportedWishlistService service,
    ILogger<GetReportedWishlistQueryHandler> logger) : IRequestHandler<GetReportedWishlistQuery, ReportedWishlistDetails>
{
    /// <summary>Retrieves the requested administrative result.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read-only result.</returns>
    public async Task<ReportedWishlistDetails> Handle(
        GetReportedWishlistQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(
            request.WishlistId,
            cancellationToken);
        ReportedWishlistLogMessages.ReportedWishlistRetrieved(
            logger,
            request.WishlistId);

        return result;
    }
}
