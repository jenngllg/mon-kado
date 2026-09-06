using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Processes leased moderation notification deliveries and bounded retention cleanup.</summary>
public interface IWishlistModerationEmailDispatcher
{
    /// <summary>Delivers a bounded batch and purges old terminal outbox entries.</summary>
    /// <param name="policy">The validated delivery policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of claims processed in this cycle.</returns>
    Task<int> DispatchAsync(
        WishlistModerationEmailDeliveryPolicy policy,
        CancellationToken cancellationToken);
}
