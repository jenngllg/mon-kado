using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents revocation of an active wishlist share link.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The wishlist identifier.</param>
/// <param name="expectedVersion">The expected optimistic concurrency version.</param>
public class DeleteWishlistShareLinkCommand(
    Guid ownerId,
    Guid wishlistId,
    uint expectedVersion) : IRequest, IGenericValidationFailure
{
    /// <summary>Gets the authenticated owner identifier.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the expected optimistic concurrency version.</summary>
    public uint ExpectedVersion { get; } = expectedVersion;

    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        if (OwnerId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new WishlistShareLinkNotFoundException();
    }
}

/// <summary>
/// Handles revocation of active wishlist share links.
/// </summary>
/// <param name="shareService">The wishlist share-link service.</param>
/// <param name="logger">The logger.</param>
public class DeleteWishlistShareLinkCommandHandler(
    IWishlistShareService shareService,
    ILogger<DeleteWishlistShareLinkCommandHandler> logger)
    : IRequestHandler<DeleteWishlistShareLinkCommand>
{
    /// <summary>Revokes the active wishlist share link.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member no longer exists.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is unavailable to the member.</exception>
    /// <exception cref="WishlistShareLinkNotFoundException">The active share link does not exist.</exception>
    /// <exception cref="WishlistShareLinkVersionConflictException">The expected version is stale.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task Handle(
        DeleteWishlistShareLinkCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishlistShareLinkDeletionStarted(
            logger,
            request.OwnerId,
            request.WishlistId);
        var deleted = await shareService.DeleteAsync(
            request.OwnerId,
            request.WishlistId,
            request.ExpectedVersion,
            cancellationToken);

        if (!deleted)
            throw new WishlistShareLinkNotFoundException();

        ApplicationLogMessages.WishlistShareLinkDeleted(
            logger,
            request.OwnerId,
            request.WishlistId);
    }
}
