using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests a reversible archive-state change for an owned wishlist.</summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The wishlist identifier.</param>
/// <param name="isArchived">The archive state validated by the common pipeline.</param>
/// <param name="expectedVersion">The client version.</param>
public class SetWishlistArchivedCommand(
    Guid ownerId,
    Guid wishlistId,
    bool? isArchived,
    uint expectedVersion) : IRequest<WishlistDetails>, IGenericValidationFailure
{
    /// <summary>Gets the owner identifier.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the requested archive state.</summary>
    public bool? IsArchived { get; } = isArchived;
    /// <summary>Gets the client version.</summary>
    public uint ExpectedVersion { get; } = expectedVersion;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>Coordinates and logs owner-controlled archiving without changing reservations or shares.</summary>
/// <param name="wishlistService">The transactional wishlist service.</param>
/// <param name="logger">The structured logger.</param>
public class SetWishlistArchivedCommandHandler(
    IWishlistService wishlistService,
    ILogger<SetWishlistArchivedCommandHandler> logger)
    : IRequestHandler<SetWishlistArchivedCommand, WishlistDetails>
{
    /// <summary>Changes the archive state validated by the pipeline.</summary>
    /// <param name="request">The requested change.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated wishlist and concurrency version.</returns>
    public async Task<WishlistDetails> Handle(
        SetWishlistArchivedCommand request,
        CancellationToken cancellationToken)
    {
        var wishlist = await wishlistService.SetArchivedAsync(
            request.OwnerId,
            request.WishlistId,
            request.IsArchived.GetValueOrDefault(),
            request.ExpectedVersion,
            cancellationToken);
        ApplicationLogMessages.WishlistArchiveStateChanged(
            logger,
            request.OwnerId,
            request.WishlistId,
            wishlist.IsArchived);

        return wishlist;
    }
}
