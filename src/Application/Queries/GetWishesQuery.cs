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
/// Represents a request for all gift wishes in an owned private wishlist.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The parent wishlist identifier.</param>
public class GetWishesQuery(
    Guid ownerId,
    Guid wishlistId) : IRequest<WishCollectionDetails>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated owner identifier.
    /// </summary>
    public Guid OwnerId { get; } = ownerId;

    /// <summary>
    /// Gets the parent wishlist identifier.
    /// </summary>
    public Guid WishlistId { get; } = wishlistId;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        if (OwnerId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        if (WishlistId == Guid.Empty)
            return new WishlistNotFoundException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles complete gift wish collection retrieval requests.
/// </summary>
/// <param name="wishService">The gift wish service.</param>
/// <param name="logger">The logger.</param>
public class GetWishesQueryHandler(
    IWishService wishService,
    ILogger<GetWishesQueryHandler> logger) : IRequestHandler<GetWishesQuery, WishCollectionDetails>
{
    /// <summary>
    /// Gets all gift wishes from an owned private wishlist.
    /// </summary>
    /// <param name="request">The collection query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete ordered collection.</returns>
    public async Task<WishCollectionDetails> Handle(
        GetWishesQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishCollectionRetrievalStarted(
            logger,
            request.OwnerId,
            request.WishlistId);
        var collection = await wishService.GetCollectionAsync(
            request.OwnerId,
            request.WishlistId,
            cancellationToken);
        ApplicationLogMessages.WishCollectionRetrieved(
            logger,
            request.OwnerId,
            request.WishlistId);

        return collection;
    }
}
