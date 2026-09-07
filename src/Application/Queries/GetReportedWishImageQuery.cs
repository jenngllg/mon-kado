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
/// <param name="wishId">The wishId.</param>
public class GetReportedWishImageQuery(
    Guid wishlistId,
    Guid wishId) : IRequest<Stream>
{
    /// <summary>Gets the wishlistId.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the wishId.</summary>
    public Guid WishId { get; } = wishId;
}

/// <summary>Coordinates validated administrative reads without logging private content.</summary>
/// <param name="service">The read service.</param>
/// <param name="logger">The structured logger.</param>
public class GetReportedWishImageQueryHandler(
    IReportedWishlistService service,
    ILogger<GetReportedWishImageQueryHandler> logger) : IRequestHandler<GetReportedWishImageQuery, Stream>
{
    /// <summary>Retrieves the requested administrative result.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read-only result.</returns>
    public async Task<Stream> Handle(
        GetReportedWishImageQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.OpenImageAsync(
            request.WishlistId,
            request.WishId,
            cancellationToken);
        ReportedWishlistLogMessages.ReportedWishImageRetrieved(
            logger,
            request.WishlistId,
            request.WishId);

        return result;
    }
}
