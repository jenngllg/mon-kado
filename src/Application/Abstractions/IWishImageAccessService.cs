namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Defines current-state access checks for signed gift-image URLs.
/// </summary>
public interface IWishImageAccessService
{
    /// <summary>
    /// Determines whether an image remains current under an owned private wishlist.
    /// </summary>
    /// <param name="ownerId">The owner identifier embedded in the signed grant.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="imageId">The immutable image identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the image remains accessible.</returns>
    Task<bool> IsOwnedImageCurrentAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether an image remains current through an active share link.
    /// </summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="imageId">The immutable image identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the image remains accessible.</returns>
    Task<bool> IsSharedImageCurrentAsync(
        Guid shareLinkId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId,
        CancellationToken cancellationToken);
}
