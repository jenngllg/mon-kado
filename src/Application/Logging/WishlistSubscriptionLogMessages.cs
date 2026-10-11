using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Defines technical-identifier-only subscription logs.</summary>
public static partial class WishlistSubscriptionLogMessages
{
    /// <summary>Logs a committed subscription creation.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The subscriber.</param>
    /// <param name="subscriptionId">The subscription.</param>
    [LoggerMessage(LogEventIds.WishlistSubscriptionCreated, LogLevel.Information, "Member {MemberId} subscribed through subscription {SubscriptionId}")]
    public static partial void Created(
        ILogger logger,
        Guid memberId,
        Guid subscriptionId);

    /// <summary>Logs a committed subscription removal.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The subscriber.</param>
    /// <param name="subscriptionId">The subscription.</param>
    [LoggerMessage(LogEventIds.WishlistSubscriptionRemoved, LogLevel.Information, "Member {MemberId} removed subscription {SubscriptionId}")]
    public static partial void Removed(
        ILogger logger,
        Guid memberId,
        Guid subscriptionId);

    /// <summary>Logs a successful subscription read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The subscriber.</param>
    /// <param name="subscriptionId">The subscription.</param>
    [LoggerMessage(LogEventIds.WishlistSubscriptionRead, LogLevel.Information, "Member {MemberId} read subscription {SubscriptionId}")]
    public static partial void Read(
        ILogger logger,
        Guid memberId,
        Guid subscriptionId);

    /// <summary>Logs a successful followed-list page read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The subscriber.</param>
    /// <param name="count">The accessible subscription count.</param>
    [LoggerMessage(LogEventIds.WishlistSubscriptionsRead, LogLevel.Information, "Member {MemberId} read followed lists with {Count} accessible subscriptions")]
    public static partial void PageRead(
        ILogger logger,
        Guid memberId,
        int count);
}
