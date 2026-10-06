using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests the public social-preview metadata.</summary>
/// <param name="shareLinkId">The share-link identifier.</param>
public class GetWishlistSharePreviewQuery(Guid shareLinkId) : IRequest<WishlistSharePreview>
{
    /// <summary>Gets the share-link identifier.</summary>
    public Guid ShareLinkId { get; } = shareLinkId;
}

/// <summary>Coordinates minimal public preview reads without returning bearer secrets.</summary>
/// <param name="service">The current-state preview reader.</param>
/// <param name="logger">The structured logger.</param>
public class GetWishlistSharePreviewQueryHandler(
    IWishlistSharePreviewService service,
    ILogger<GetWishlistSharePreviewQueryHandler> logger) : IRequestHandler<GetWishlistSharePreviewQuery, WishlistSharePreview>
{
    /// <summary>Reads the validated public preview.</summary>
    /// <param name="request">The validated query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The minimal public preview.</returns>
    /// <exception cref="SharedWishlistNotFoundException">Sharing is unavailable.</exception>
    public async Task<WishlistSharePreview> Handle(
        GetWishlistSharePreviewQuery request,
        CancellationToken cancellationToken)
    {
        var preview = await service.GetAsync(
            request.ShareLinkId,
            cancellationToken) ?? throw new SharedWishlistNotFoundException();
        ApplicationLogMessages.WishlistSharePreviewRetrieved(
            logger,
            request.ShareLinkId);

        return preview;
    }
}
