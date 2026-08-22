using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>
/// Defines structured application log messages.
/// </summary>
public static partial class ApplicationLogMessages
{
    /// <summary>
    /// Logs the start of a current session retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.CurrentSessionRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving the current session for member {MemberId}.")]
    public static partial void CurrentSessionRetrievalStarted(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a successful current session retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.CurrentSessionRetrieved,
        Level = LogLevel.Information,
        Message = "Current session retrieved for member {MemberId}.")]
    public static partial void CurrentSessionRetrieved(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs the start of a current session logout.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.CurrentSessionLogoutStarted,
        Level = LogLevel.Debug,
        Message = "Logging out the current browser session.")]
    public static partial void CurrentSessionLogoutStarted(ILogger logger);

    /// <summary>
    /// Logs a completed current session logout.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.CurrentSessionLogoutCompleted,
        Level = LogLevel.Information,
        Message = "Current browser session logout completed.")]
    public static partial void CurrentSessionLogoutCompleted(ILogger logger);

    /// <summary>
    /// Logs the start of a member profile update.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberProfileUpdateStarted,
        Level = LogLevel.Debug,
        Message = "Updating the profile for member {MemberId}.")]
    public static partial void MemberProfileUpdateStarted(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a successful member profile update.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberProfileUpdated,
        Level = LogLevel.Information,
        Message = "Profile updated for member {MemberId}.")]
    public static partial void MemberProfileUpdated(
        ILogger logger,
        Guid memberId);
}
