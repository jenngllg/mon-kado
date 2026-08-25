using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Creates and retrieves gift wishes in private wishlists.
/// </summary>
public interface IWishService
{
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
}
