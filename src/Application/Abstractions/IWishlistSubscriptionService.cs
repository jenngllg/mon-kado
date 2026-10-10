using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Manages subscriptions without creating participants or reservations.</summary>
public interface IWishlistSubscriptionService
{
    /// <summary>Subscribes a member to a verified current share link.</summary>
    /// <param name="memberId">The authenticated member.</param>
    /// <param name="shareLinkId">The presented share link.</param>
    /// <param name="secret">The presented bearer secret.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed subscription summary.</returns>
    Task<WishlistSubscriptionDetails> CreateAsync(
        Guid memberId,
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken);

    /// <summary>Gets a member's accessible subscription.</summary>
    /// <param name="memberId">The authenticated member.</param>
    /// <param name="id">The subscription identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary, or null without disclosing foreign subscriptions.</returns>
    Task<WishlistSubscriptionDetails?> GetAsync(
        Guid memberId,
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Gets the current subscription using a verified share capability.</summary>
    /// <param name="memberId">The authenticated member.</param>
    /// <param name="shareLinkId">The presented share link.</param>
    /// <param name="secret">The bearer secret.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current subscription, or null.</returns>
    Task<WishlistSubscriptionDetails?> GetCurrentAsync(
        Guid memberId,
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken);

    /// <summary>Gets an ordered page of accessible followed lists.</summary>
    /// <param name="memberId">The authenticated member.</param>
    /// <param name="page">The one-based page.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The accessible subscription page.</returns>
    Task<WishlistSubscriptionPage> GetPageAsync(
        Guid memberId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Removes only the member's subscription.</summary>
    /// <param name="memberId">The authenticated member.</param>
    /// <param name="id">The subscription identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether a subscription was removed.</returns>
    Task<bool> DeleteAsync(
        Guid memberId,
        Guid id,
        CancellationToken cancellationToken);
}
