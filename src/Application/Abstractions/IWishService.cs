using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Creates, retrieves, updates, and deletes gift wishes in private wishlists.
/// </summary>
public interface IWishService
{
    /// <summary>Removes a gift image and schedules durable file cleanup.</summary>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The gift identifier.</param>
    /// <param name="expectedVersion">The expected gift version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated gift, or null when the gift does not exist.</returns>
    Task<WishDetails?> DeleteImageAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all gift wishes from an owned private wishlist.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete ordered collection.</returns>
    /// <exception cref="Common.Exceptions.InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    /// <exception cref="Common.Exceptions.WishlistNotFoundException">The parent wishlist is unavailable to the owner.</exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishCollectionDetails> GetCollectionAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reorders all gift wishes from an owned private wishlist.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishIds">All current wish identifiers in their requested final order.</param>
    /// <param name="expectedVersion">The collection version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete updated order.</returns>
    /// <exception cref="Common.Exceptions.InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    /// <exception cref="Common.Exceptions.WishlistNotFoundException">The parent wishlist is unavailable to the owner.</exception>
    /// <exception cref="Common.Exceptions.WishOrderConflictException">The requested identifiers do not match the collection.</exception>
    /// <exception cref="Common.Exceptions.WishOrderVersionConflictException">The collection version is stale.</exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishOrderDetails> ReorderAsync(
        Guid ownerId,
        Guid wishlistId,
        IReadOnlyCollection<Guid> wishIds,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a gift wish in an owned private wishlist.
    /// </summary>
    /// <param name="id">The generated wish identifier.</param>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="name">The normalized display name.</param>
    /// <param name="note">The normalized optional note.</param>
    /// <param name="url">The normalized optional product URL.</param>
    /// <param name="price">The optional price in euros.</param>
    /// <param name="quantity">The total desired quantity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created wish, or <see langword="null" /> when the wishlist is unavailable to the owner.</returns>
    /// <exception cref="Common.Exceptions.InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        string name,
        string? note,
        string? url,
        decimal? price,
        int quantity,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a gift wish from its parent wishlist.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wish when found under the parent; otherwise, <see langword="null" />.</returns>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishDetails?> GetAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates a gift wish in an owned private wishlist.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="name">The normalized display name.</param>
    /// <param name="note">The normalized optional note.</param>
    /// <param name="url">The normalized optional product URL.</param>
    /// <param name="price">The optional price in euros.</param>
    /// <param name="quantity">The total desired quantity.</param>
    /// <param name="expectedVersion">The version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated wish, or <see langword="null" /> when the wish does not exist under an owned parent.</returns>
    /// <exception cref="Common.Exceptions.InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    /// <exception cref="Common.Exceptions.WishlistNotFoundException">The parent wishlist is unavailable to the owner.</exception>
    /// <exception cref="Common.Exceptions.WishVersionConflictException">The wish version is stale.</exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishDetails?> UpdateAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        string name,
        string? note,
        string? url,
        decimal? price,
        int quantity,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds or replaces the normalized image of an owned gift wish.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="imageId">The generated immutable image identifier.</param>
    /// <param name="contentHash">The SHA-256 hash of the normalized WebP content.</param>
    /// <param name="expectedVersion">The version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated wish, or <see langword="null" /> when the wish is unavailable.</returns>
    /// <exception cref="Common.Exceptions.InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    /// <exception cref="Common.Exceptions.WishlistNotFoundException">The parent wishlist is unavailable to the owner.</exception>
    /// <exception cref="Common.Exceptions.WishVersionConflictException">The wish version is stale.</exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishDetails?> UpsertImageAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId,
        byte[] contentHash,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a gift wish from an owned private wishlist.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="expectedVersion">The version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the wish was deleted; otherwise, <see langword="false" />.</returns>
    /// <exception cref="Common.Exceptions.InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    /// <exception cref="Common.Exceptions.WishlistNotFoundException">The parent wishlist is unavailable to the owner.</exception>
    /// <exception cref="Common.Exceptions.WishVersionConflictException">The wish version is stale.</exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<bool> DeleteAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        uint expectedVersion,
        CancellationToken cancellationToken);
}
