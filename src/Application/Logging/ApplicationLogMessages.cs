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
}
