using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Creates PostgreSQL transactions and row locks for reservation mutations.
/// </summary>
public interface IGiftReservationTransactionFactory
{
    /// <summary>Begins a reservation transaction.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created transaction.</returns>
    Task<IGiftReservationTransaction> BeginAsync(CancellationToken cancellationToken);

    /// <summary>Locks a share link.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The locked share link when found.</returns>
    Task<WishlistShareLink?> LockShareLinkAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken);

    /// <summary>Locks a gift wish.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The locked gift wish when found.</returns>
    Task<Wish?> LockWishAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken);
}
