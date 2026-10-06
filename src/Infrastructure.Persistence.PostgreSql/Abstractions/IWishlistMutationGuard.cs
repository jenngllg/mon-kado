namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Revalidates writable owner access under transaction-scoped account and parent locks.</summary>
public interface IWishlistMutationGuard
{
    /// <summary>Locks an owned parent for archiving, restoring or deleting, while rejecting suspension.</summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when the parent is locked.</returns>
    Task LockStateChangeAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>Locks the owner account and wishlist, then rejects suspended or inaccessible resources.</summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when the wishlist is locked and writable.</returns>
    Task LockAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken);
}
