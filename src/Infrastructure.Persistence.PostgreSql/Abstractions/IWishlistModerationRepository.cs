using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Coordinates the persisted inputs and durable effects of moderation decisions.</summary>
public interface IWishlistModerationRepository
{
    /// <summary>Locks the deciding account before acquiring any wishlist lock.</summary>
    /// <param name="administratorId">The deciding account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the account still exists.</returns>
    Task<bool> LockAdministratorAsync(
        Guid administratorId,
        CancellationToken cancellationToken);
    /// <summary>Locks and tracks the wishlist until transaction completion.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current wishlist, or null if absent.</returns>
    Task<Wishlist?> LockWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);
    /// <summary>Reads a detached current wishlist.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current wishlist, or null if absent.</returns>
    Task<Wishlist?> GetWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);
    /// <summary>Reads a detached committed event for ambiguous-commit reconciliation.</summary>
    /// <param name="eventId">The attempted event identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed event, or null if absent.</returns>
    Task<WishlistModerationEvent?> GetEventAsync(
        Guid eventId,
        CancellationToken cancellationToken);
    /// <summary>Allocates the next event sequence while the parent wishlist is locked.</summary>
    /// <param name="wishlistId">The locked wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next sequence number.</returns>
    Task<long> GetNextSequenceAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);
    /// <summary>Stages an event and its unique notification without saving.</summary>
    /// <param name="decision">The decision to persist atomically with its wishlist.</param>
    void AddDecision(WishlistModerationEvent decision);
    /// <summary>Reads a page of private decisions and their matching total count.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The bounded page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The newest-first page.</returns>
    Task<WishlistModerationEventPage> GetEventsAsync(
        Guid wishlistId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
