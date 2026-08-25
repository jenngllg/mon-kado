using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines PostgreSQL persistence operations for gift wishes.
/// </summary>
public interface IWishRepository
{
    /// <summary>
    /// Allocates the next stable position for a parent wishlist atomically.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The allocated positive position.</returns>
    Task<long> AllocatePositionAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds a gift wish to the current unit of work.
    /// </summary>
    /// <param name="wish">The wish to add.</param>
    void Add(Wish wish);

    /// <summary>
    /// Gets a gift wish under a specific parent wishlist without tracking it.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wish when found under the parent; otherwise, <see langword="null" />.</returns>
    Task<Wish?> GetByIdAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken);
}
