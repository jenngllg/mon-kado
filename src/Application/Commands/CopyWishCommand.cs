using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests an independent copy of one shared wish.</summary>
/// <param name="ownerId">The authenticated destination owner.</param>
/// <param name="wishlistId">The destination list.</param>
/// <param name="sourceShareLinkId">The nullable source link identifier.</param>
/// <param name="sourceWishId">The nullable source wish identifier.</param>
/// <param name="secret">The nullable source bearer secret.</param>
public class CopyWishCommand(
    Guid ownerId,
    Guid wishlistId,
    Guid? sourceShareLinkId,
    Guid? sourceWishId,
    string? secret) : IRequest<WishDetails>
{
    /// <summary>Gets the authenticated destination owner.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the destination list.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the source link identifier.</summary>
    public Guid? SourceShareLinkId { get; } = sourceShareLinkId;
    /// <summary>Gets the source wish identifier.</summary>
    public Guid? SourceWishId { get; } = sourceWishId;
    /// <summary>Gets the source bearer secret.</summary>
    public string? Secret { get; } = secret;
}

/// <summary>Handles explicit shared-wish copy commands.</summary>
/// <param name="copyService">The atomic copy service.</param>
/// <param name="logger">The structured logger.</param>
public class CopyWishCommandHandler(
    IWishCopyService copyService,
    ILogger<CopyWishCommandHandler> logger) : IRequestHandler<CopyWishCommand, WishDetails>
{
    /// <summary>Creates an independent wish after centralized input validation.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created wish.</returns>
    public async Task<WishDetails> Handle(
        CopyWishCommand request,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        ApplicationLogMessages.WishCreationStarted(
            logger,
            request.OwnerId,
            request.WishlistId,
            id);
        var wish = await copyService.CopyAsync(
            id,
            request.OwnerId,
            request.WishlistId,
            request.SourceShareLinkId.GetValueOrDefault(),
            request.SourceWishId.GetValueOrDefault(),
            request.Secret ?? string.Empty,
            cancellationToken);
        ApplicationLogMessages.WishCreated(
            logger,
            request.OwnerId,
            request.WishlistId,
            id);

        return wish;
    }
}
