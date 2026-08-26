using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines PostgreSQL persistence operations for wishlist share links.
/// </summary>
public interface IWishlistShareLinkRepository
{
    /// <summary>Adds a share link to the current unit of work.</summary>
    /// <param name="shareLink">The share link.</param>
    void Add(WishlistShareLink shareLink);

    /// <summary>Removes a share link from the current unit of work.</summary>
    /// <param name="shareLink">The share link.</param>
    void Remove(WishlistShareLink shareLink);

    /// <summary>Gets a share link by identifier without tracking it.</summary>
    /// <param name="id">The share-link identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The share link, or <see langword="null" />.</returns>
    Task<WishlistShareLink?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Gets a share link by wishlist without tracking it.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The share link, or <see langword="null" />.</returns>
    Task<WishlistShareLink?> GetByWishlistIdAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>Gets a tracked share link for update.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The share link, or <see langword="null" />.</returns>
    Task<WishlistShareLink?> GetByWishlistIdForUpdateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>Gets the public content of a wishlist.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public wishlist, or <see langword="null" />.</returns>
    Task<SharedWishlistDetails?> GetSharedWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);
}
