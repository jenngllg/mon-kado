using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Creates PostgreSQL transactions for wishlist report creation.
/// </summary>
public interface IWishlistReportTransactionFactory
{
    /// <summary>
    /// Begins a wishlist report transaction.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created transaction.</returns>
    Task<IWishlistReportTransaction> BeginAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Locks a share link while its secret is verified and a report is persisted.
    /// </summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked share link when found.</returns>
    Task<WishlistShareLink?> LockShareLinkAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken);
}
