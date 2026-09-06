using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Logs moderation operations without private reasons or owner content.</summary>
public static partial class WishlistModerationLogMessages
{
    /// <summary>Logs a provider-acknowledged notification.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="eventId">The moderation event identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistModerationEmailSent, Level = LogLevel.Information, Message = "Moderation notification {EventId} acknowledged by provider.")]
    public static partial void EmailSent(
        ILogger logger,
        Guid eventId);
    /// <summary>Logs only an allowlisted failure category, never raw provider exception data.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="eventId">The moderation event identifier.</param>
    /// <param name="failure">The allowlisted technical classification.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistModerationEmailFailed, Level = LogLevel.Error, Message = "Moderation notification {EventId} failed: {Failure}.")]
    public static partial void EmailFailed(
        ILogger logger,
        Guid eventId,
        WishlistModerationEmailFailure failure);
    /// <summary>Logs a delivery cycle failure without raw exception messages or private SQL values.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistModerationEmailCycleFailed, Level = LogLevel.Error, Message = "Moderation notification delivery cycle failed.")]
    public static partial void EmailCycleFailed(ILogger logger);
    /// <summary>Logs the beginning of an administrator decision.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="administratorId">The administrator identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistModerationUpdateStarted, Level = LogLevel.Debug, Message = "Administrator {AdministratorId} started moderation of wishlist {WishlistId}.")]
    public static partial void UpdateStarted(
        ILogger logger,
        Guid administratorId,
        Guid wishlistId);
    /// <summary>Logs a completed administrator decision.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="administratorId">The administrator identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistModerationUpdated, Level = LogLevel.Information, Message = "Administrator {AdministratorId} completed moderation of wishlist {WishlistId}.")]
    public static partial void Updated(
        ILogger logger,
        Guid administratorId,
        Guid wishlistId);
    /// <summary>Logs a private moderation-state read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistModerationRetrieved, Level = LogLevel.Information, Message = "Retrieved moderation state of wishlist {WishlistId}.")]
    public static partial void Retrieved(
        ILogger logger,
        Guid wishlistId);
    /// <summary>Logs a private moderation-history read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistModerationEventsRetrieved, Level = LogLevel.Information, Message = "Retrieved moderation history of wishlist {WishlistId}.")]
    public static partial void EventsRetrieved(
        ILogger logger,
        Guid wishlistId);
}
