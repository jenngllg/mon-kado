using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Records administrative operations without request references or exported content.</summary>
public static partial class AdministrativeDataExportLogMessages
{
    /// <summary>Records an accepted administrative request.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="administratorId">The actor identifier.</param>
    /// <param name="memberId">The target identifier.</param>
    /// <param name="exportId">The export identifier.</param>
    [LoggerMessage(LogEventIds.AdministrativeDataExportRequested, LogLevel.Information, "Administrator {AdministratorId} requested export {ExportId} for member {MemberId}")]
    public static partial void Requested(
        ILogger logger,
        Guid administratorId,
        Guid memberId,
        Guid exportId);
    /// <summary>Records a metadata read.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="administratorId">The actor identifier.</param>
    /// <param name="memberId">The target identifier.</param>
    /// <param name="exportId">The export identifier.</param>
    [LoggerMessage(LogEventIds.AdministrativeDataExportRead, LogLevel.Information, "Administrator {AdministratorId} read export {ExportId} for member {MemberId}")]
    public static partial void Read(
        ILogger logger,
        Guid administratorId,
        Guid memberId,
        Guid exportId);
    /// <summary>Records release of a download stream, not confirmation of receipt.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="administratorId">The actor identifier.</param>
    /// <param name="memberId">The target identifier.</param>
    /// <param name="exportId">The export identifier.</param>
    [LoggerMessage(LogEventIds.AdministrativeDataExportDownload, LogLevel.Information, "Administrator {AdministratorId} started downloading export {ExportId} for member {MemberId}")]
    public static partial void DownloadStarted(
        ILogger logger,
        Guid administratorId,
        Guid memberId,
        Guid exportId);
}
