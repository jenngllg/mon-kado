using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Manages durable notification leases, current delivery recipients and retention.</summary>
public interface IWishlistModerationEmailRepository
{
    /// <summary>Atomically claims the oldest eligible decision for a wishlist.</summary>
    /// <param name="now">The current UTC date.</param>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new lease, or null when no eligible work remains.</returns>
    Task<WishlistModerationEmailClaim?> ClaimAsync(
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
    /// <summary>Loads current owner delivery data only while the lease and resource remain valid.</summary>
    /// <param name="claim">The current claim.</param>
    /// <param name="now">The current UTC date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The private message, or null when it must no longer be sent.</returns>
    Task<WishlistModerationEmailMessage?> GetMessageAsync(
        WishlistModerationEmailClaim claim,
        DateTime now,
        CancellationToken cancellationToken);
    /// <summary>Records a result only if the caller still owns the unexpired lease.</summary>
    /// <param name="claim">The lease being completed.</param>
    /// <param name="now">The current UTC date.</param>
    /// <param name="isTerminal">Whether this delivery is permanently processed.</param>
    /// <param name="retryAt">The next eligible UTC attempt date.</param>
    /// <param name="failure">The bounded technical classification, or null on success.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after the conditional update.</returns>
    Task CompleteAsync(
        WishlistModerationEmailClaim claim,
        DateTime now,
        bool isTerminal,
        DateTime retryAt,
        string? failure,
        CancellationToken cancellationToken);
    /// <summary>Deletes a bounded batch of old terminal deliveries without deleting decision history.</summary>
    /// <param name="processedBefore">The exclusive UTC retention boundary.</param>
    /// <param name="batchSize">The maximum number of deletions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after the bounded cleanup.</returns>
    Task PurgeAsync(
        DateTime processedBefore,
        int batchSize,
        CancellationToken cancellationToken);
}
