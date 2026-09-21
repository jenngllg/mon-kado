using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;

/// <summary>Contains one operation's actual progress and next expected execution.</summary>
[ExcludeFromCodeCoverage]
public class WorkerTelemetrySnapshot
{
    /// <summary>Gets the running, waiting or disabled state.</summary>
    public string State { get; init; } = "waiting";
    /// <summary>Gets the consecutive failed cycle count.</summary>
    public long ConsecutiveFailures
    {
        get; init;
    }
    /// <summary>Gets the current or last cycle start time.</summary>
    public DateTime? StartedAt
    {
        get; init;
    }
    /// <summary>Gets the next expected cycle time.</summary>
    public DateTime? NextExpectedAt
    {
        get; init;
    }
    /// <summary>Gets the last completed cycle time.</summary>
    public DateTime? LastCompletedAt
    {
        get; init;
    }
    /// <summary>Gets the successful cycle count.</summary>
    public long Successes
    {
        get; init;
    }
    /// <summary>Gets the failed cycle count.</summary>
    public long Failures
    {
        get; init;
    }
    /// <summary>Gets the permanently failed work item count.</summary>
    public long TerminalFailures
    {
        get; init;
    }
    /// <summary>Gets the last cycle's monotonic duration.</summary>
    public double LastDurationMilliseconds
    {
        get; init;
    }
}
