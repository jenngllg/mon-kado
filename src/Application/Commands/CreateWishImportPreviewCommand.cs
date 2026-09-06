using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests editable suggestions for an already authorized wishlist.</summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The authorized parent wishlist identifier.</param>
/// <param name="url">The merchant URL.</param>
public class CreateWishImportPreviewCommand(
    Guid ownerId,
    Guid wishlistId,
    string? url) : IRequest<WishImportPreview>
{
    /// <summary>Gets the authenticated owner identifier.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the authorized wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the merchant URL.</summary>
    public string? Url { get; } = url;
}

/// <summary>Coordinates a preview without creating or updating a wish.</summary>
/// <param name="importService">The merchant import service.</param>
/// <param name="logger">The structured application logger.</param>
public class CreateWishImportPreviewCommandHandler(
    IWishImportService importService,
    ILogger<CreateWishImportPreviewCommandHandler> logger) : IRequestHandler<CreateWishImportPreviewCommand, WishImportPreview>
{
    /// <summary>Produces suggestions after centralized validation and authorization.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The editable preview.</returns>
    public async Task<WishImportPreview> Handle(
        CreateWishImportPreviewCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishImportPreviewStarted(
            logger,
            request.OwnerId,
            request.WishlistId);
        var preview = await importService.PreviewAsync(
            request.Url!,
            cancellationToken);
        ApplicationLogMessages.WishImportPreviewCreated(
            logger,
            request.OwnerId,
            request.WishlistId);

        return preview;
    }
}
