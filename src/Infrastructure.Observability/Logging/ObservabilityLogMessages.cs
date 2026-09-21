using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Logging;

/// <summary>Defines bounded local monitoring diagnostics.</summary>
public static partial class ObservabilityLogMessages
{
    /// <summary>Reports a publication failure without exception messages or filesystem paths.</summary>
    /// <param name="logger">The scoped logger.</param>
    [LoggerMessage(EventId = LogEventIds.TelemetrySnapshotFailed, Level = LogLevel.Error, Message = "Private telemetry snapshot publication failed")]
    public static partial void SnapshotFailed(ILogger logger);
}
