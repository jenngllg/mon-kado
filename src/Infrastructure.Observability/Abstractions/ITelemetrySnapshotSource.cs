using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;

/// <summary>Provides atomic process-local telemetry snapshots.</summary>
public interface ITelemetrySnapshotSource
{
    /// <summary>Copies a consistent snapshot without accessing application persistence.</summary>
    /// <returns>The immutable current snapshot.</returns>
    ApplicationTelemetrySnapshot Capture();
}
