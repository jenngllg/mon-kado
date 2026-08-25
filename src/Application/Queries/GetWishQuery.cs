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
/// Represents a request to retrieve one gift wish from a private wishlist.
/// </summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="wishlistId">The parent wishlist identifier.</param>
/// <param name="wishId">The wish identifier.</param>
public class GetWishQuery(
    Guid memberId,
    Guid wishlistId,
    Guid wishId) : IRequest<WishDetails>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated member identifier.
    /// </summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>
    /// Gets the parent wishlist identifier.
    /// </summary>
    public Guid WishlistId { get; } = wishlistId;

    /// <summary>
    /// Gets the wish identifier.
    /// </summary>
    public Guid WishId { get; } = wishId;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new WishNotFoundException();
    }
}

/// <summary>
/// Handles retrieval of one gift wish from a private wishlist.
/// </summary>
/// <param name="wishService">The wish service.</param>
/// <param name="logger">The logger.</param>
public class GetWishQueryHandler(
    IWishService wishService,
    ILogger<GetWishQueryHandler> logger)
    : IRequestHandler<GetWishQuery, WishDetails>
{
    /// <summary>
    /// Gets a gift wish after owner authorization on its parent wishlist.
    /// </summary>
    /// <param name="request">The wish query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested wish.</returns>
    /// <exception cref="WishNotFoundException">The wish is unavailable under the parent wishlist.</exception>
    public async Task<WishDetails> Handle(
        GetWishQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishRetrievalStarted(
            logger,
            request.MemberId,
            request.WishlistId,
            request.WishId);
        var wish = await wishService.GetAsync(
            request.WishlistId,
            request.WishId,
            cancellationToken);

        if (wish is null)
            throw new WishNotFoundException();

        ApplicationLogMessages.WishRetrieved(
            logger,
            request.MemberId,
            request.WishlistId,
            request.WishId);

        return wish;
    }
}
