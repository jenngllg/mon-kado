using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to replace the complete order of a gift wish collection.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The parent wishlist identifier.</param>
/// <param name="wishIds">All current wish identifiers in their requested final order.</param>
/// <param name="expectedVersion">The collection version supplied by the client.</param>
public class ReorderWishesCommand(
    Guid ownerId,
    Guid wishlistId,
    IReadOnlyCollection<Guid>? wishIds,
    uint expectedVersion) : IRequest<WishOrderDetails>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated owner identifier.
    /// </summary>
    public Guid OwnerId { get; } = ownerId;

    /// <summary>
    /// Gets the parent wishlist identifier.
    /// </summary>
    public Guid WishlistId { get; } = wishlistId;

    /// <summary>
    /// Gets all current wish identifiers in their requested final order.
    /// </summary>
    public IReadOnlyCollection<Guid>? WishIds { get; } = wishIds;

    /// <summary>
    /// Gets the collection version supplied by the client.
    /// </summary>
    public uint ExpectedVersion { get; } = expectedVersion;

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
/// Handles complete gift wish collection reorder requests.
/// </summary>
/// <param name="wishService">The gift wish service.</param>
/// <param name="logger">The logger.</param>
public class ReorderWishesCommandHandler(
    IWishService wishService,
    ILogger<ReorderWishesCommandHandler> logger) : IRequestHandler<ReorderWishesCommand, WishOrderDetails>
{
    /// <summary>
    /// Replaces the complete order of a gift wish collection.
    /// </summary>
    /// <param name="request">The reorder command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete updated order.</returns>
    public async Task<WishOrderDetails> Handle(
        ReorderWishesCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishReorderStarted(
            logger,
            request.OwnerId,
            request.WishlistId);
        var order = await wishService.ReorderAsync(
            request.OwnerId,
            request.WishlistId,
            request.WishIds!,
            request.ExpectedVersion,
            cancellationToken);
        ApplicationLogMessages.WishReordered(
            logger,
            request.OwnerId,
            request.WishlistId);

        return order;
    }
}
