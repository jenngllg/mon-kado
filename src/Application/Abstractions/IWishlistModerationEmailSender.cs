using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Sends one French moderation notification without transparent provider retries.</summary>
public interface IWishlistModerationEmailSender
{
    /// <summary>Sends a notification using its event identity as the stable message identity.</summary>
    /// <param name="message">The private delivery message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after the provider acknowledges delivery.</returns>
    Task SendAsync(
        WishlistModerationEmailMessage message,
        CancellationToken cancellationToken);
}
