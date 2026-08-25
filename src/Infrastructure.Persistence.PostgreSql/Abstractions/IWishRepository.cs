using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

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
    /// Removes a tracked gift wish from the current unit of work.
    /// </summary>
    /// <param name="wish">The wish to remove.</param>
    void Remove(Wish wish);

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

    /// <summary>
    /// Gets a tracked gift wish under a specific parent for an optimistic update.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked wish when found under the parent; otherwise, <see langword="null" />.</returns>
    Task<Wish?> GetByIdForUpdateAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all gift wishes from a parent wishlist without tracking them.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete collection ordered by position.</returns>
    Task<IReadOnlyCollection<Wish>> GetByWishlistIdAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets and locks all tracked gift wishes from a parent wishlist.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete collection ordered by position.</returns>
    Task<IReadOnlyCollection<Wish>> GetByWishlistIdForUpdateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the collection state without tracking it.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection state when found; otherwise, <see langword="null" />.</returns>
    Task<WishPositionSequence?> GetCollectionStateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets and locks the tracked collection state.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The locked collection state when found; otherwise, <see langword="null" />.</returns>
    Task<WishPositionSequence?> GetCollectionStateForUpdateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reloads a tracked collection state after database triggers changed it.
    /// </summary>
    /// <param name="sequence">The tracked collection state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous reload.</returns>
    Task ReloadCollectionStateAsync(
        WishPositionSequence sequence,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clears all tracked persistence entities before ambiguous-commit reconciliation.
    /// </summary>
    void ClearTracking();
}
