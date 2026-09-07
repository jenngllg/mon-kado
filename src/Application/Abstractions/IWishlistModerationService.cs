using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Reads and changes wishlist moderation with durable history and notification delivery.</summary>
public interface IWishlistModerationService
{
    /// <summary>Reads the current moderation state.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current private moderation state.</returns>
    Task<WishlistModerationDetails> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);
    /// <summary>Atomically records a moderation decision and its notification.</summary>
    /// <param name="administratorId">The authenticated administrator identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="isSuspended">The requested suspension state.</param>
    /// <param name="reason">The normalized and validated private reason.</param>
    /// <param name="expectedVersion">The required optimistic concurrency version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated private moderation state.</returns>
    Task<WishlistModerationDetails> UpdateAsync(
        Guid administratorId,
        Guid wishlistId,
        bool isSuspended,
        string? reason,
        uint expectedVersion,
        CancellationToken cancellationToken);
    /// <summary>Reads administrator-only history in reverse decision order.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The maximum number of items.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page of durable decisions.</returns>
    Task<WishlistModerationEventPage> GetEventsAsync(
        Guid wishlistId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
