using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Worker.Logging;
/// <summary>
/// Represents worker log messages.
/// </summary>

public static partial class WorkerLogMessages
{
    /// <summary>
    /// Executes the authentication email delivery disabled operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.AuthenticationEmailDeliveryDisabled,
        Level = LogLevel.Information,
        Message = "Authentication email delivery is disabled for this environment.")]
    public static partial void AuthenticationEmailDeliveryDisabled(ILogger logger);
    /// <summary>
    /// Executes the authentication email delivery failed operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type.</param>
    /// <param name="exception">The delivery exception.</param>

    [LoggerMessage(
        EventId = LogEventIds.AuthenticationEmailDeliveryFailed,
        Level = LogLevel.Error,
        Message = "Authentication email delivery failed and will be retried. Exception type: {ExceptionType}")]
    public static partial void AuthenticationEmailDeliveryFailed(
        ILogger logger,
        string exceptionType,
        Exception exception);
    /// <summary>
    /// Executes the expired accounts deleted operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="deletedAccountCount">The deleted account count.</param>

    [LoggerMessage(
        EventId = LogEventIds.ExpiredAccountsDeleted,
        Level = LogLevel.Information,
        Message = "Deleted {DeletedAccountCount} expired unconfirmed accounts.")]
    public static partial void ExpiredAccountsDeleted(
        ILogger logger,
        int deletedAccountCount);
    /// <summary>
    /// Executes the expired account cleanup failed operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type.</param>
    /// <param name="exception">The cleanup exception.</param>

    [LoggerMessage(
        EventId = LogEventIds.ExpiredAccountCleanupFailed,
        Level = LogLevel.Error,
        Message = "Expired account cleanup failed and will be retried. Exception type: {ExceptionType}")]
    public static partial void ExpiredAccountCleanupFailed(
        ILogger logger,
        string exceptionType,
        Exception exception);
    /// <summary>
    /// Executes the expired sessions deleted operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="deletedSessionCount">The deleted session count.</param>

    [LoggerMessage(
        EventId = LogEventIds.ExpiredSessionsDeleted,
        Level = LogLevel.Information,
        Message = "Deleted {DeletedSessionCount} expired authentication sessions.")]
    public static partial void ExpiredSessionsDeleted(
        ILogger logger,
        int deletedSessionCount);
    /// <summary>
    /// Executes the expired session cleanup failed operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type.</param>
    /// <param name="exception">The cleanup exception.</param>

    [LoggerMessage(
        EventId = LogEventIds.ExpiredSessionCleanupFailed,
        Level = LogLevel.Error,
        Message = "Authentication session cleanup failed and will be retried. Exception type: {ExceptionType}")]
    public static partial void ExpiredSessionCleanupFailed(
        ILogger logger,
        string exceptionType,
        Exception exception);
}
