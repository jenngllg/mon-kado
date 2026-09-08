using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Logs audit reads without exposing the journal or filter values.</summary>
public static partial class AdministrativeAuditLogMessages
{
    /// <summary>Records a successful administrative audit read.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="administratorId">The authenticated reader.</param>
    [LoggerMessage(
        LogEventIds.AdministrativeAuditRetrieved,
        LogLevel.Information,
        "Administrator {AdministratorId} retrieved the administrative audit")]
    public static partial void Retrieved(
        ILogger logger,
        Guid administratorId);
}
