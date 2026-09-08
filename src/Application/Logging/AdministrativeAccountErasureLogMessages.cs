using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Records erasure outcomes without retained account content or request references.</summary>
public static partial class AdministrativeAccountErasureLogMessages
{
    /// <summary>Records a delivery failure without recipient or exception data.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="operationId">The erasure identifier.</param>
    /// <param name="failure">The bounded technical classification.</param>
    [LoggerMessage(LogEventIds.AccountErasureNotificationFailed, LogLevel.Error, "Erasure notification {OperationId} failed with classification {Failure}")]
    public static partial void NotificationFailed(
        ILogger logger,
        Guid operationId,
        AccountErasureEmailFailure failure);
    /// <summary>Records abandoned notifications without exposing recipient data.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="count">The number of newly abandoned notifications.</param>
    [LoggerMessage(LogEventIds.AccountErasureNotificationsAbandoned, LogLevel.Error, "Abandoned {Count} erasure notifications and discarded their recipients")]
    public static partial void NotificationsAbandoned(
        ILogger logger,
        int count);
    /// <summary>Records provider acceptance after a fenced acknowledgement.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="operationId">The erasure identifier.</param>
    [LoggerMessage(LogEventIds.AccountErasureNotificationAccepted, LogLevel.Information, "Erasure notification {OperationId} accepted by the provider")]
    public static partial void NotificationAccepted(
        ILogger logger,
        Guid operationId);
    /// <summary>Records a failed cycle without provider or database exception text.</summary>
    /// <param name="logger">The correlated logger.</param>
    [LoggerMessage(LogEventIds.AccountErasureProcessingFailed, LogLevel.Error, "Account erasure processing cycle failed")]
    public static partial void ProcessingFailed(ILogger logger);
    /// <summary>Records committed administrative erasure.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="memberId">The erased target.</param>
    /// <param name="operationId">The durable operation.</param>
    [LoggerMessage(LogEventIds.AdministrativeAccountErasureExecuted, LogLevel.Information, "Administrator {AdministratorId} erased member {MemberId} in operation {OperationId}")]
    public static partial void Executed(
        ILogger logger,
        Guid administratorId,
        Guid memberId,
        Guid operationId);
}
