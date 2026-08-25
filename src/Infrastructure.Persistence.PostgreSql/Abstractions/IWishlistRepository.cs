using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines PostgreSQL persistence operations for private wishlists.
/// </summary>
public interface IWishlistRepository
{
    /// <summary>
    /// Adds a wishlist to the current unit of work.
    /// </summary>
    /// <param name="wishlist">The wishlist to add.</param>
    void Add(Wishlist wishlist);

    /// <summary>
    /// Gets a wishlist without tracking it.
    /// </summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wishlist when found; otherwise, <see langword="null" />.</returns>
    Task<Wishlist?> GetByIdAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all private wishlists owned by a member in reverse creation order.
    /// </summary>
    /// <param name="ownerId">The owner member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The ordered wishlists, an empty collection when the member owns none, or
    /// <see langword="null" /> when the member no longer exists.
    /// </returns>
    Task<IReadOnlyCollection<Wishlist>?> GetByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a member's access to a private wishlist.
    /// </summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The owner access result.</returns>
    Task<WishlistAccess> GetAccessAsync(
        Guid memberId,
        Guid wishlistId,
        CancellationToken cancellationToken);
}
