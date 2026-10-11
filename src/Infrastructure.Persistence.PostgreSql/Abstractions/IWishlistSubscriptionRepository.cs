using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Persists scoped, capability-bound wishlist subscriptions.</summary>
public interface IWishlistSubscriptionRepository
{
    /// <summary>Tracks a new subscription.</summary>
    /// <param name="subscription">The subscription.</param>
    void Add(WishlistSubscription subscription);

    /// <summary>Tracks subscription removal.</summary>
    /// <param name="subscription">The subscription.</param>
    void Remove(WishlistSubscription subscription);

    /// <summary>Gets a tracked member-owned subscription.</summary>
    /// <param name="memberId">The subscriber.</param>
    /// <param name="id">The subscription.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The owned subscription or null.</returns>
    Task<WishlistSubscription?> GetForUpdateAsync(
        Guid memberId,
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Gets one currently accessible member-owned summary.</summary>
    /// <param name="memberId">The subscriber.</param>
    /// <param name="id">The subscription.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary or null.</returns>
    Task<WishlistSubscriptionDetails?> GetAsync(
        Guid memberId,
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Gets the accessible subscription to the specified share link.</summary>
    /// <param name="memberId">The subscriber.</param>
    /// <param name="shareLinkId">The accepted share link.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary or null.</returns>
    Task<WishlistSubscriptionDetails?> GetCurrentAsync(
        Guid memberId,
        Guid shareLinkId,
        CancellationToken cancellationToken);

    /// <summary>Gets the accessible summaries and pagination metadata in one database snapshot.</summary>
    /// <param name="memberId">The subscriber.</param>
    /// <param name="page">The requested page.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page.</returns>
    Task<WishlistSubscriptionPage> GetPageAsync(
        Guid memberId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
