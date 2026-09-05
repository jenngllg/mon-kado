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
/// Represents a request to add or replace a gift-wish image.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The parent wishlist identifier.</param>
/// <param name="wishId">The gift-wish identifier.</param>
/// <param name="image">The untrusted source image bytes.</param>
/// <param name="expectedVersion">The version supplied by the client.</param>
/// <param name="hasValidMultipartShape">Whether the multipart request contains only the expected image field.</param>
public class UpsertWishImageCommand(
    Guid ownerId,
    Guid wishlistId,
    Guid wishId,
    byte[]? image,
    uint expectedVersion,
    bool hasValidMultipartShape) : IRequest<WishDetails>, IGenericValidationFailure
{
    /// <summary>Gets the authenticated owner identifier.</summary>
    public Guid OwnerId { get; } = ownerId;

    /// <summary>Gets the parent wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;

    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid WishId { get; } = wishId;

    /// <summary>Gets the untrusted source image bytes.</summary>
    public byte[] Image { get; } = image ?? [];

    /// <summary>Gets the version supplied by the client.</summary>
    public uint ExpectedVersion { get; } = expectedVersion;

    /// <summary>Gets whether the multipart request contains only the expected image field.</summary>
    public bool HasValidMultipartShape { get; } = hasValidMultipartShape;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        if (OwnerId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        if (WishlistId == Guid.Empty)
            return new WishlistNotFoundException();

        if (WishId == Guid.Empty)
            return new WishNotFoundException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles gift-wish image add and replacement requests.
/// </summary>
/// <param name="processor">The untrusted image processor.</param>
/// <param name="store">The durable image store.</param>
/// <param name="wishService">The gift-wish persistence service.</param>
/// <param name="timeProvider">The time provider.</param>
/// <param name="logger">The logger.</param>
public class UpsertWishImageCommandHandler(
    IGiftImageProcessor processor,
    IGiftImageStore store,
    IWishService wishService,
    TimeProvider timeProvider,
    ILogger<UpsertWishImageCommandHandler> logger)
    : IRequestHandler<UpsertWishImageCommand, WishDetails>
{
    /// <summary>
    /// Validates, normalizes, stores, and attaches an image to a gift wish.
    /// </summary>
    /// <param name="request">The image upsert request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete updated gift wish.</returns>
    public async Task<WishDetails> Handle(
        UpsertWishImageCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.GiftImageUpsertStarted(
            logger,
            request.OwnerId,
            request.WishlistId,
            request.WishId);
        var processedImage = await processor.ProcessAsync(
            request.Image,
            cancellationToken);
        var imageId = Guid.CreateVersion7(timeProvider.GetUtcNow());
        await store.WritePendingAsync(
            imageId,
            processedImage.Content,
            cancellationToken);
        var wish = await wishService.UpsertImageAsync(
            request.OwnerId,
            request.WishlistId,
            request.WishId,
            imageId,
            processedImage.ContentHash,
            request.ExpectedVersion,
            cancellationToken);

        if (wish is null)
            throw new WishNotFoundException();

        await ReconcilePendingImageAsync(
            imageId,
            wish.ImageId == imageId,
            cancellationToken);
        ApplicationLogMessages.GiftImageUpserted(
            logger,
            request.OwnerId,
            request.WishlistId,
            request.WishId);

        return wish;
    }

    /// <summary>
    /// Removes or confirms the pending image after the database outcome is known.
    /// </summary>
    /// <param name="imageId">The newly written image identifier.</param>
    /// <param name="wasCommitted">Whether the image became the current database reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous reconciliation.</returns>
    private async Task ReconcilePendingImageAsync(
        Guid imageId,
        bool wasCommitted,
        CancellationToken cancellationToken)
    {
        try
        {
            if (wasCommitted)
            {
                await store.MarkCommittedAsync(
                    imageId,
                    cancellationToken);

                return;
            }

            await store.DeleteAsync(
                imageId,
                cancellationToken);
        }
        catch (GiftImageStorageUnavailableException exception)
        {
            ApplicationLogMessages.GiftImagePendingCleanupFailed(
                logger,
                imageId,
                exception);
        }
    }
}
