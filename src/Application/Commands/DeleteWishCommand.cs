using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to delete a gift wish.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The parent wishlist identifier.</param>
/// <param name="wishId">The wish identifier.</param>
/// <param name="expectedVersion">The version supplied by the client.</param>
public class DeleteWishCommand(
    Guid ownerId,
    Guid wishlistId,
    Guid wishId,
    uint expectedVersion) : IRequest, IGenericValidationFailure
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
    /// Gets the wish identifier.
    /// </summary>
    public Guid WishId { get; } = wishId;

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

        if (WishlistId == Guid.Empty)
            return new WishlistNotFoundException();

        return new WishNotFoundException();
    }
}

/// <summary>
/// Handles gift wish deletion requests.
/// </summary>
/// <param name="wishService">The wish service.</param>
/// <param name="logger">The logger.</param>
public class DeleteWishCommandHandler(
    IWishService wishService,
    ILogger<DeleteWishCommandHandler> logger)
    : IRequestHandler<DeleteWishCommand>
{
    /// <summary>
    /// Deletes a gift wish from an owned private wishlist.
    /// </summary>
    /// <param name="request">The gift wish deletion command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="WishlistNotFoundException">The parent wishlist is unavailable to the member.</exception>
    /// <exception cref="WishNotFoundException">The wish is unavailable under the parent wishlist.</exception>
    /// <exception cref="WishVersionConflictException">The wish version is stale.</exception>
    /// <exception cref="InvalidAuthenticationSessionException">The member was deleted during the deletion.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task Handle(
        DeleteWishCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishDeletionStarted(
            logger,
            request.OwnerId,
            request.WishlistId,
            request.WishId);
        var deleted = await wishService.DeleteAsync(
            request.OwnerId,
            request.WishlistId,
            request.WishId,
            request.ExpectedVersion,
            cancellationToken);

        if (!deleted)
            throw new WishNotFoundException();

        ApplicationLogMessages.WishDeleted(
            logger,
            request.OwnerId,
            request.WishlistId,
            request.WishId);
    }
}
