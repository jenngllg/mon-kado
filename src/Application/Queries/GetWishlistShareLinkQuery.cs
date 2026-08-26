using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>
/// Represents retrieval of an owned wishlist's active share link.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The wishlist identifier.</param>
public class GetWishlistShareLinkQuery(
    Guid ownerId,
    Guid wishlistId) : IRequest<WishlistShareLinkDetails>, IGenericValidationFailure
{
    /// <summary>Gets the authenticated owner identifier.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;

    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        if (OwnerId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new WishlistNotFoundException();
    }
}

/// <summary>
/// Handles owner retrieval of active wishlist share links.
/// </summary>
/// <param name="shareService">The wishlist share-link service.</param>
/// <param name="logger">The logger.</param>
public class GetWishlistShareLinkQueryHandler(
    IWishlistShareService shareService,
    ILogger<GetWishlistShareLinkQueryHandler> logger)
    : IRequestHandler<GetWishlistShareLinkQuery, WishlistShareLinkDetails>
{
    /// <summary>Gets the active share link.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active share link.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member no longer exists.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is unavailable to the member.</exception>
    /// <exception cref="WishlistShareLinkNotFoundException">The active share link does not exist.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<WishlistShareLinkDetails> Handle(
        GetWishlistShareLinkQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishlistShareLinkRetrievalStarted(
            logger,
            request.OwnerId,
            request.WishlistId);
        var shareLink = await shareService.GetAsync(
            request.OwnerId,
            request.WishlistId,
            cancellationToken) ?? throw new WishlistShareLinkNotFoundException();
        ApplicationLogMessages.WishlistShareLinkRetrieved(
            logger,
            request.OwnerId,
            request.WishlistId,
            shareLink.Id);

        return shareLink;
    }
}
