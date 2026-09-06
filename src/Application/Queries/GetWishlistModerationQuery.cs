using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests the private moderation state of a wishlist.</summary>
/// <param name="wishlistId">The wishlist identifier.</param>
public class GetWishlistModerationQuery(Guid wishlistId) : IRequest<WishlistModerationDetails>
{
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;
}

/// <summary>Coordinates administrator moderation-state reads.</summary>
/// <param name="service">The moderation service.</param>
/// <param name="logger">The structured logger.</param>
public class GetWishlistModerationQueryHandler(
    IWishlistModerationService service,
    ILogger<GetWishlistModerationQueryHandler> logger) : IRequestHandler<GetWishlistModerationQuery, WishlistModerationDetails>
{
    /// <summary>Reads the current moderation state.</summary>
    /// <param name="request">The validated query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The private moderation state.</returns>
    public async Task<WishlistModerationDetails> Handle(
        GetWishlistModerationQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(
            request.WishlistId,
            cancellationToken);
        WishlistModerationLogMessages.Retrieved(
            logger,
            request.WishlistId);

        return result;
    }
}
