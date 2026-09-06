using JennGllg.Fr.MonKado.Back.Api.Models;

namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>
/// Creates and validates short-lived signed gift-image URLs.
/// </summary>
public interface IWishImageUrlService
{
    /// <summary>
    /// Creates an absolute private image URL for an authenticated owner response.
    /// </summary>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="imageId">The immutable image identifier.</param>
    /// <returns>The absolute signed URL.</returns>
    string CreateOwnedUrl(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId);

    /// <summary>
    /// Creates an absolute image URL scoped to an active share link.
    /// </summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="imageId">The immutable image identifier.</param>
    /// <returns>The absolute signed URL.</returns>
    string CreateSharedUrl(
        Guid shareLinkId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId);

    /// <summary>
    /// Validates a signed owner grant against its route identifiers.
    /// </summary>
    /// <param name="token">The signed bearer token.</param>
    /// <param name="wishlistId">The route wishlist identifier.</param>
    /// <param name="wishId">The route gift-wish identifier.</param>
    /// <returns>The validated grant.</returns>
    WishImageGrant ValidateOwned(
        string? token,
        Guid wishlistId,
        Guid wishId);

    /// <summary>
    /// Validates a signed shared grant against its route identifiers.
    /// </summary>
    /// <param name="token">The signed bearer token.</param>
    /// <param name="shareLinkId">The route share-link identifier.</param>
    /// <param name="wishId">The route gift-wish identifier.</param>
    /// <returns>The validated grant.</returns>
    WishImageGrant ValidateShared(
        string? token,
        Guid shareLinkId,
        Guid wishId);
}
