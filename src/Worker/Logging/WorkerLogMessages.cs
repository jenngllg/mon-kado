using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Models;

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
    /// Logs a delivered account confirmation email.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="outboxMessageId">The outbox message identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.AccountConfirmationEmailSent,
        Level = LogLevel.Information,
        Message = "Account confirmation email {OutboxMessageId} sent.")]
    public static partial void AccountConfirmationEmailSent(
        ILogger logger,
        Guid outboxMessageId);
    /// <summary>
    /// Logs a delivered account password reset email.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="outboxMessageId">The outbox message identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.AccountPasswordResetEmailSent,
        Level = LogLevel.Information,
        Message = "Account password reset email {OutboxMessageId} sent.")]
    public static partial void AccountPasswordResetEmailSent(
        ILogger logger,
        Guid outboxMessageId);
    /// <summary>
    /// Logs a delivered member email change confirmation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="outboxMessageId">The outbox message identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberEmailChangeConfirmationSent,
        Level = LogLevel.Information,
        Message = "Member email change confirmation {OutboxMessageId} sent.")]
    public static partial void MemberEmailChangeConfirmationSent(
        ILogger logger,
        Guid outboxMessageId);
    /// <summary>
    /// Logs a delivered member email change security notification.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="outboxMessageId">The outbox message identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberEmailChangeSecurityNotificationSent,
        Level = LogLevel.Information,
        Message = "Member email change security notification {OutboxMessageId} sent.")]
    public static partial void MemberEmailChangeSecurityNotificationSent(
        ILogger logger,
        Guid outboxMessageId);
    /// <summary>
    /// Logs a delivered member password change security notification.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="outboxMessageId">The outbox message identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberPasswordChangedSecurityNotificationSent,
        Level = LogLevel.Information,
        Message = "Member password change security notification {OutboxMessageId} sent.")]
    public static partial void MemberPasswordChangedSecurityNotificationSent(
        ILogger logger,
        Guid outboxMessageId);
    /// <summary>
    /// Logs an authentication email rejected by Gmail.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="outboxMessageId">The outbox message identifier.</param>
    /// <param name="failureCategory">The technical failure category.</param>
    [LoggerMessage(
        EventId = LogEventIds.AuthenticationEmailProviderRejectedMessage,
        Level = LogLevel.Error,
        Message = "Authentication email {OutboxMessageId} was rejected by the provider with category {FailureCategory}.")]
    public static partial void AuthenticationEmailProviderRejectedMessage(
        ILogger logger,
        Guid outboxMessageId,
        AuthenticationEmailFailureCategory failureCategory);
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

    /// <summary>
    /// Logs deleted member email change requests.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="deletedRequestCount">The deleted request count.</param>
    [LoggerMessage(
        EventId = LogEventIds.ExpiredMemberEmailChangeRequestsDeleted,
        Level = LogLevel.Information,
        Message = "Deleted {DeletedRequestCount} expired member email change requests.")]
    public static partial void ExpiredMemberEmailChangeRequestsDeleted(
        ILogger logger,
        int deletedRequestCount);

    /// <summary>
    /// Logs a member email change request cleanup failure.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type.</param>
    /// <param name="exception">The cleanup exception.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberEmailChangeRequestCleanupFailed,
        Level = LogLevel.Error,
        Message = "Member email change request cleanup failed and will be retried. Exception type: {ExceptionType}")]
    public static partial void MemberEmailChangeRequestCleanupFailed(
        ILogger logger,
        string exceptionType,
        Exception exception);

    /// <summary>
    /// Logs deleted processed authentication emails.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="deletedEmailCount">The deleted email count.</param>
    [LoggerMessage(
        EventId = LogEventIds.ProcessedAuthenticationEmailsDeleted,
        Level = LogLevel.Information,
        Message = "Deleted {DeletedEmailCount} processed authentication email messages.")]
    public static partial void ProcessedAuthenticationEmailsDeleted(
        ILogger logger,
        int deletedEmailCount);

    /// <summary>
    /// Logs a processed authentication email cleanup failure.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type.</param>
    /// <param name="exception">The cleanup exception.</param>
    [LoggerMessage(
        EventId = LogEventIds.ProcessedAuthenticationEmailCleanupFailed,
        Level = LogLevel.Error,
        Message = "Processed authentication email cleanup failed and will be retried. Exception type: {ExceptionType}")]
    public static partial void ProcessedAuthenticationEmailCleanupFailed(
        ILogger logger,
        string exceptionType,
        Exception exception);
}
