using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;

/// <summary>Publishes private process snapshots without modifying application storage.</summary>
public interface ITelemetrySnapshotStore
{
    /// <summary>Atomically replaces the current private snapshot.</summary>
    /// <param name="snapshot">The bounded snapshot.</param>
    /// <param name="cancellationToken">The shutdown token.</param>
    /// <returns>The asynchronous write.</returns>
    Task WriteAsync(
        ApplicationTelemetrySnapshot snapshot,
        CancellationToken cancellationToken);
}
