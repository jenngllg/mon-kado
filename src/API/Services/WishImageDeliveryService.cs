using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Models;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Revalidates current PostgreSQL access before reading normalized image content.
/// </summary>
/// <param name="accessService">The current-state access service.</param>
/// <param name="store">The durable image store.</param>
public class WishImageDeliveryService(
    IWishImageAccessService accessService,
    IGiftImageStore store) : IWishImageDeliveryService
{
    /// <inheritdoc />
    public async Task<Stream> OpenOwnedAsync(
        WishImageGrant grant,
        CancellationToken cancellationToken)
    {
        var isCurrent = grant.OwnerId is Guid ownerId &&
            await accessService.IsOwnedImageCurrentAsync(
                ownerId,
                grant.WishlistId,
                grant.WishId,
                grant.ImageId,
                cancellationToken);

        return await OpenCurrentAsync(
            grant.ImageId,
            isCurrent,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenSharedAsync(
        WishImageGrant grant,
        CancellationToken cancellationToken)
    {
        var isCurrent = grant.ShareLinkId is Guid shareLinkId &&
            await accessService.IsSharedImageCurrentAsync(
                shareLinkId,
                grant.WishlistId,
                grant.WishId,
                grant.ImageId,
                cancellationToken);

        return await OpenCurrentAsync(
            grant.ImageId,
            isCurrent,
            cancellationToken);
    }

    /// <summary>
    /// Opens an image after its current database reference has been confirmed.
    /// </summary>
    /// <param name="imageId">The image identifier.</param>
    /// <param name="isCurrent">Whether the signed grant still matches current state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The readable image stream.</returns>
    /// <exception cref="GiftImageNotFoundException">The signed grant is no longer current.</exception>
    /// <exception cref="GiftImageStorageUnavailableException">The referenced image is missing from storage.</exception>
    private async Task<Stream> OpenCurrentAsync(
        Guid imageId,
        bool isCurrent,
        CancellationToken cancellationToken)
    {
        if (!isCurrent)
            throw new GiftImageNotFoundException();

        var stream = await store.OpenReadAsync(
            imageId,
            cancellationToken);

        if (stream is null)
        {
            throw new GiftImageStorageUnavailableException(
                new FileNotFoundException("A referenced normalized gift image is missing."));
        }

        return stream;
    }
}
