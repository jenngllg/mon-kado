using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Creates PostgreSQL transactions for participant operations.
/// </summary>
public interface IWishlistParticipantTransactionFactory
{
    /// <summary>Begins a participant transaction.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created transaction.</returns>
    Task<IWishlistParticipantTransaction> BeginAsync(CancellationToken cancellationToken);

    /// <summary>Locks a share-link row before validating a participant mutation.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked share link when found.</returns>
    Task<WishlistShareLink?> LockShareLinkAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken);

    /// <summary>Locks a wishlist row for participant creation.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wishlist owner identifier.</returns>
    Task<Guid> LockWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);
}
