using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests an independent copy between two lists owned by one member.</summary>
/// <param name="ownerId">The authenticated owner.</param>
/// <param name="sourceWishlistId">The source list.</param>
/// <param name="sourceWishId">The source wish.</param>
/// <param name="destinationWishlistId">The nullable destination list.</param>
public class CopyOwnedWishCommand(
    Guid ownerId,
    Guid sourceWishlistId,
    Guid sourceWishId,
    Guid? destinationWishlistId) : IRequest<WishDetails>
{
    /// <summary>Gets the authenticated owner.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the source list.</summary>
    public Guid SourceWishlistId { get; } = sourceWishlistId;
    /// <summary>Gets the source wish.</summary>
    public Guid SourceWishId { get; } = sourceWishId;
    /// <summary>Gets the destination list.</summary>
    public Guid? DestinationWishlistId { get; } = destinationWishlistId;
}

/// <summary>Handles explicit copies between owned lists.</summary>
/// <param name="copyService">The atomic copy service.</param>
/// <param name="logger">The structured logger.</param>
public class CopyOwnedWishCommandHandler(
    IWishCopyService copyService,
    ILogger<CopyOwnedWishCommandHandler> logger) : IRequestHandler<CopyOwnedWishCommand, WishDetails>
{
    /// <summary>Copies the server-owned source after centralized input validation.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created wish.</returns>
    public async Task<WishDetails> Handle(
        CopyOwnedWishCommand request,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var destinationId = request.DestinationWishlistId.GetValueOrDefault();
        ApplicationLogMessages.WishCreationStarted(
            logger,
            request.OwnerId,
            destinationId,
            id);
        var wish = await copyService.CopyOwnedAsync(
            id,
            request.OwnerId,
            destinationId,
            request.SourceWishlistId,
            request.SourceWishId,
            cancellationToken);
        ApplicationLogMessages.WishCreated(
            logger,
            request.OwnerId,
            destinationId,
            id);

        return wish;
    }
}
