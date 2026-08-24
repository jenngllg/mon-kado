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
/// Represents a request to retrieve a private wishlist.
/// </summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="wishlistId">The wishlist identifier.</param>
public class GetWishlistQuery(
    Guid memberId,
    Guid wishlistId) : IRequest<WishlistDetails>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated member identifier.
    /// </summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>
    /// Gets the wishlist identifier.
    /// </summary>
    public Guid WishlistId { get; } = wishlistId;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new WishlistNotFoundException();
    }
}

/// <summary>
/// Handles private wishlist retrieval requests.
/// </summary>
/// <param name="wishlistService">The wishlist service.</param>
/// <param name="logger">The logger.</param>
public class GetWishlistQueryHandler(
    IWishlistService wishlistService,
    ILogger<GetWishlistQueryHandler> logger)
    : IRequestHandler<GetWishlistQuery, WishlistDetails>
{
    /// <summary>
    /// Gets a private wishlist after owner authorization.
    /// </summary>
    /// <param name="request">The wishlist query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The private wishlist.</returns>
    /// <exception cref="WishlistNotFoundException">The wishlist no longer exists.</exception>
    public async Task<WishlistDetails> Handle(
        GetWishlistQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishlistRetrievalStarted(
            logger,
            request.MemberId,
            request.WishlistId);
        var wishlist = await wishlistService.GetAsync(
            request.WishlistId,
            cancellationToken);

        if (wishlist is null)
            throw new WishlistNotFoundException();

        ApplicationLogMessages.WishlistRetrieved(
            logger,
            request.MemberId,
            request.WishlistId);

        return wishlist;
    }
}
