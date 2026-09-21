using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Logging;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

/// <summary>Publishes process-local snapshots independently of business cycles.</summary>
/// <param name="source">The bounded process counters.</param>
/// <param name="store">The private atomic snapshot writer.</param>
/// <param name="timeProvider">The periodic scheduling clock.</param>
/// <param name="options">The validated local settings.</param>
/// <param name="logger">The safe operational logger.</param>
public class TelemetrySnapshotWorker(
    ITelemetrySnapshotSource source,
    ITelemetrySnapshotStore store,
    TimeProvider timeProvider,
    IOptions<ObservabilityOptions> options,
    ILogger<TelemetrySnapshotWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        if (!options.Value.Enabled)
            return;
        using var timer = new PeriodicTimer(
            options.Value.Interval,
            timeProvider);
        try
        {
            while (true)
            {
                using var activity = new Activity("TelemetrySnapshot")
                    .SetIdFormat(ActivityIdFormat.W3C)
                    .Start();
                using var scope = logger.BeginScope(new Dictionary<string, object>
                {
                    ["CorrelationId"] = Guid.CreateVersion7().ToString("D"),
                    ["TraceId"] = activity.TraceId.ToString()
                });
                try
                {
                    await store.WriteAsync(
                        source.Capture(),
                        stoppingToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // A stale file is an explicit host-monitor incident; never fabricate fresh zero counters.
                    ObservabilityLogMessages.SnapshotFailed(logger);
                }
                // This worker owns the timer; only cancellation can end its active wait.
                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown is not a failed business cycle.
        }
    }
}
