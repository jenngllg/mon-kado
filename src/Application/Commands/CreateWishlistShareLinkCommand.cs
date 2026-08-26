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
/// Represents creation of the active share link of an owned wishlist.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The wishlist identifier.</param>
public class CreateWishlistShareLinkCommand(
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
/// Handles creation of wishlist share links.
/// </summary>
/// <param name="shareService">The wishlist share-link service.</param>
/// <param name="logger">The logger.</param>
public class CreateWishlistShareLinkCommandHandler(
    IWishlistShareService shareService,
    ILogger<CreateWishlistShareLinkCommandHandler> logger)
    : IRequestHandler<CreateWishlistShareLinkCommand, WishlistShareLinkDetails>
{
    /// <summary>Creates the active share link.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created share link.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member no longer exists.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is unavailable to the member.</exception>
    /// <exception cref="WishlistShareLinkAlreadyExistsException">An active share link already exists.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<WishlistShareLinkDetails> Handle(
        CreateWishlistShareLinkCommand request,
        CancellationToken cancellationToken)
    {
        var shareLinkId = Guid.CreateVersion7();
        ApplicationLogMessages.WishlistShareLinkCreationStarted(
            logger,
            request.OwnerId,
            request.WishlistId,
            shareLinkId);
        var shareLink = await shareService.CreateAsync(
            shareLinkId,
            request.OwnerId,
            request.WishlistId,
            cancellationToken) ?? throw new WishlistNotFoundException();
        ApplicationLogMessages.WishlistShareLinkCreated(
            logger,
            request.OwnerId,
            request.WishlistId,
            shareLink.Id);

        return shareLink;
    }
}
