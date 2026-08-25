using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to delete a private wishlist.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The wishlist identifier.</param>
/// <param name="expectedVersion">The version supplied by the client.</param>
public class DeleteWishlistCommand(
    Guid ownerId,
    Guid wishlistId,
    uint expectedVersion) : IRequest, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated owner identifier.
    /// </summary>
    public Guid OwnerId { get; } = ownerId;

    /// <summary>
    /// Gets the wishlist identifier.
    /// </summary>
    public Guid WishlistId { get; } = wishlistId;

    /// <summary>
    /// Gets the version supplied by the client.
    /// </summary>
    public uint ExpectedVersion { get; } = expectedVersion;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        if (OwnerId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new WishlistNotFoundException();
    }
}

/// <summary>
/// Handles private wishlist deletion requests.
/// </summary>
/// <param name="wishlistService">The wishlist service.</param>
/// <param name="logger">The logger.</param>
public class DeleteWishlistCommandHandler(
    IWishlistService wishlistService,
    ILogger<DeleteWishlistCommandHandler> logger)
    : IRequestHandler<DeleteWishlistCommand>
{
    /// <summary>
    /// Deletes a private wishlist owned by the authenticated member.
    /// </summary>
    /// <param name="request">The wishlist deletion command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="WishlistNotFoundException">The wishlist is unavailable to the member.</exception>
    /// <exception cref="WishlistVersionConflictException">The wishlist version is stale.</exception>
    /// <exception cref="InvalidAuthenticationSessionException">The member was deleted during the deletion.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task Handle(
        DeleteWishlistCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishlistDeletionStarted(
            logger,
            request.OwnerId,
            request.WishlistId);
        var deleted = await wishlistService.DeleteAsync(
            request.OwnerId,
            request.WishlistId,
            request.ExpectedVersion,
            cancellationToken);

        if (!deleted)
            throw new WishlistNotFoundException();

        ApplicationLogMessages.WishlistDeleted(
            logger,
            request.OwnerId,
            request.WishlistId);
    }
}
