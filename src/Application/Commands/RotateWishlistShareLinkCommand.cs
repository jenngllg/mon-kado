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
/// Represents rotation of an active wishlist share link.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The wishlist identifier.</param>
/// <param name="expectedVersion">The expected optimistic concurrency version.</param>
public class RotateWishlistShareLinkCommand(
    Guid ownerId,
    Guid wishlistId,
    uint expectedVersion) : IRequest<WishlistShareLinkDetails>, IGenericValidationFailure
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
/// Handles rotation of active wishlist share links.
/// </summary>
/// <param name="shareService">The wishlist share-link service.</param>
/// <param name="logger">The logger.</param>
public class RotateWishlistShareLinkCommandHandler(
    IWishlistShareService shareService,
    ILogger<RotateWishlistShareLinkCommandHandler> logger)
    : IRequestHandler<RotateWishlistShareLinkCommand, WishlistShareLinkDetails>
{
    /// <summary>Rotates the active share-link secret.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rotated share link.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member no longer exists.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is unavailable to the member.</exception>
    /// <exception cref="WishlistShareLinkNotFoundException">The active share link does not exist.</exception>
    /// <exception cref="WishlistShareLinkVersionConflictException">The expected version is stale.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<WishlistShareLinkDetails> Handle(
        RotateWishlistShareLinkCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishlistShareLinkRotationStarted(
            logger,
            request.OwnerId,
            request.WishlistId);
        var shareLink = await shareService.RotateAsync(
            request.OwnerId,
            request.WishlistId,
            request.ExpectedVersion,
            cancellationToken) ?? throw new WishlistShareLinkNotFoundException();
        ApplicationLogMessages.WishlistShareLinkRotated(
            logger,
            request.OwnerId,
            request.WishlistId,
            shareLink.Id);

        return shareLink;
    }
}
