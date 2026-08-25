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
/// Represents a request to retrieve all private wishlists owned by the current member.
/// </summary>
/// <param name="memberId">The authenticated member identifier.</param>
public class GetWishlistsQuery(Guid memberId)
    : IRequest<IReadOnlyCollection<WishlistDetails>>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated member identifier.
    /// </summary>
    public Guid MemberId { get; } = memberId;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        return new InvalidAuthenticationSessionException();
    }
}

/// <summary>
/// Handles owned wishlist collection retrieval requests.
/// </summary>
/// <param name="wishlistService">The wishlist service.</param>
/// <param name="logger">The logger.</param>
public class GetWishlistsQueryHandler(
    IWishlistService wishlistService,
    ILogger<GetWishlistsQueryHandler> logger)
    : IRequestHandler<GetWishlistsQuery, IReadOnlyCollection<WishlistDetails>>
{
    /// <summary>
    /// Gets all private wishlists owned by the authenticated member.
    /// </summary>
    /// <param name="request">The wishlist collection query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The owned wishlists in reverse creation order.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    public async Task<IReadOnlyCollection<WishlistDetails>> Handle(
        GetWishlistsQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishlistCollectionRetrievalStarted(
            logger,
            request.MemberId);
        var wishlists = await wishlistService.GetByOwnerIdAsync(
            request.MemberId,
            cancellationToken);

        if (wishlists is null)
            throw new InvalidAuthenticationSessionException();

        ApplicationLogMessages.WishlistCollectionRetrieved(
            logger,
            request.MemberId);

        return wishlists;
    }
}
