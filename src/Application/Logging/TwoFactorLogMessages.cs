using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Logs second-factor operations without credentials, proofs or recovery material.</summary>
public static partial class TwoFactorLogMessages
{
    /// <summary>Logs an incoming operation without recording its sensitive input.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.TwoFactorOperationStarted,
        Level = LogLevel.Debug,
        Message = "Processing a second-factor operation.")]
    public static partial void OperationStarted(ILogger logger);

    /// <summary>Completes a verified sign-in or advances a recovery flow.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.TwoFactorCompletionProcessed,
        Level = LogLevel.Information,
        Message = "Completes a verified sign-in or advances a recovery flow.")]
    public static partial void TwoFactorCompletionProcessed(ILogger logger);

    /// <summary>Retrieves candidate authenticator material for an authorized setup flow.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.TwoFactorSetupPrepared,
        Level = LogLevel.Information,
        Message = "Retrieves candidate authenticator material for an authorized setup flow.")]
    public static partial void TwoFactorSetupPrepared(ILogger logger);

    /// <summary>Confirms a new authenticator and returns recovery codes once.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.TwoFactorSetupConfirmed,
        Level = LogLevel.Information,
        Message = "Confirms a new authenticator and returns recovery codes once.")]
    public static partial void TwoFactorSetupConfirmed(ILogger logger);

    /// <summary>Replaces recovery codes after an operation-specific reauthentication.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.TwoFactorRecoveryCodesRegenerated,
        Level = LogLevel.Information,
        Message = "Replaces recovery codes after an operation-specific reauthentication.")]
    public static partial void TwoFactorRecoveryCodesRegenerated(ILogger logger);

    /// <summary>Verifies the current factor and binds a management grant to one operation.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.TwoFactorManagementAuthorized,
        Level = LogLevel.Information,
        Message = "Verifies the current factor and binds a management grant to one operation.")]
    public static partial void TwoFactorManagementAuthorized(ILogger logger);

    /// <summary>Reads the current member's non-secret second-factor status.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.TwoFactorStatusRetrieved,
        Level = LogLevel.Information,
        Message = "Reads the current member's non-secret second-factor status.")]
    public static partial void TwoFactorStatusRetrieved(ILogger logger);
    /// <summary>Records delivery without recipient, credential or flow data.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="outboxMessageId">The technical delivery identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.TwoFactorSecurityNotificationSent,
        Level = LogLevel.Information,
        Message = "Second-factor security notification {OutboxMessageId} sent.")]
    public static partial void SecurityNotificationSent(
        ILogger logger,
        Guid outboxMessageId);
}
