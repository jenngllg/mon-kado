using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Records export lifecycle events without archive content, credentials or storage paths.</summary>
public static partial class PersonalDataExportLogMessages
{
    /// <summary>Records a storage failure after download headers were sent.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="exportId">The export identifier.</param>
    [LoggerMessage(LogEventIds.PersonalDataExportDownloadFailed, LogLevel.Error, "Personal data export {ExportId} download aborted because storage is unavailable")]
    public static partial void DownloadFailed(
        ILogger logger,
        Guid exportId);
    /// <summary>Records an accepted request or reuse.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="userId">The technical account identifier.</param>
    /// <param name="exportId">The export identifier.</param>
    [LoggerMessage(LogEventIds.PersonalDataExportRequested, LogLevel.Information, "Personal data export {ExportId} requested by member {UserId}")]
    public static partial void Requested(
        ILogger logger,
        Guid userId,
        Guid exportId);
    /// <summary>Records a successful metadata read.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="userId">The technical account identifier.</param>
    /// <param name="exportId">The export identifier.</param>
    [LoggerMessage(LogEventIds.PersonalDataExportRead, LogLevel.Information, "Personal data export {ExportId} read by member {UserId}")]
    public static partial void Read(
        ILogger logger,
        Guid userId,
        Guid exportId);
    /// <summary>Records an authorized download start, not successful receipt by the client.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="userId">The technical account identifier.</param>
    /// <param name="exportId">The export identifier.</param>
    [LoggerMessage(LogEventIds.PersonalDataExportDownload, LogLevel.Information, "Personal data export {ExportId} download started by member {UserId}")]
    public static partial void DownloadStarted(
        ILogger logger,
        Guid userId,
        Guid exportId);
    /// <summary>Records a newly claimed attempt.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="exportId">The export identifier.</param>
    [LoggerMessage(LogEventIds.PersonalDataExportAttemptStarted, LogLevel.Information, "Personal data export {ExportId} generation started")]
    public static partial void AttemptStarted(
        ILogger logger,
        Guid exportId);
    /// <summary>Records a confirmed publication.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="exportId">The export identifier.</param>
    [LoggerMessage(LogEventIds.PersonalDataExportReady, LogLevel.Information, "Personal data export {ExportId} is ready")]
    public static partial void Ready(
        ILogger logger,
        Guid exportId);
    /// <summary>Records a failed attempt without serializing an exception.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="exportId">The export identifier.</param>
    /// <param name="category">The bounded technical classification.</param>
    [LoggerMessage(LogEventIds.PersonalDataExportAttemptFailed, LogLevel.Error, "Personal data export {ExportId} attempt failed with category {Category}")]
    public static partial void AttemptFailed(
        ILogger logger,
        Guid exportId,
        string category);
    /// <summary>Records a failed cycle without serializing an exception.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="category">The bounded technical classification.</param>
    [LoggerMessage(LogEventIds.PersonalDataExportCycleFailed, LogLevel.Error, "Personal data export cycle failed with category {Category}")]
    public static partial void CycleFailed(
        ILogger logger,
        string category);
    /// <summary>Records delivery of an archive-ready notification.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="messageId">The technical outbox identifier.</param>
    [LoggerMessage(LogEventIds.PersonalDataExportNotificationSent, LogLevel.Information, "Personal data export notification {MessageId} sent")]
    public static partial void NotificationSent(
        ILogger logger,
        Guid messageId);
}
