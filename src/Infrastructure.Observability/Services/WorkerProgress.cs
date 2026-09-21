using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

/// <summary>Tracks a single operation under the owning telemetry service's lock.</summary>
public class WorkerProgress(TimeProvider timeProvider) : IWorkerProgress
{
    private string _state = "waiting";
    private DateTime? _startedAt;
    private DateTime? _nextExpectedAt = timeProvider.GetUtcNow().UtcDateTime;
    private DateTime? _lastCompletedAt;
    private long _startedTimestamp;
    private long _successes;
    private long _failures;
    private long _terminalFailures;
    private long _consecutiveFailures;
    private double _lastDurationMilliseconds;

    /// <summary>Marks the beginning of actual processing.</summary>
    public void Begin()
    {
        _state = "running";
        _startedAt = timeProvider.GetUtcNow().UtcDateTime;
        _startedTimestamp = timeProvider.GetTimestamp();
        _nextExpectedAt = null;
    }

    /// <summary>Records completion and the scheduled next execution.</summary>
    /// <param name="nextDelay">The configured delay.</param>
    /// <param name="successful">Whether the cycle succeeded.</param>
    /// <returns>The monotonic elapsed milliseconds.</returns>
    public double Complete(
        TimeSpan nextDelay,
        bool successful)
    {
        _lastDurationMilliseconds = timeProvider.GetElapsedTime(_startedTimestamp).TotalMilliseconds;
        _lastCompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        _nextExpectedAt = _lastCompletedAt.Value + nextDelay;
        _state = "waiting";

        if (successful)
        {
            _successes++;
            _consecutiveFailures = 0;
        }
        else
        {
            _failures++;
            _consecutiveFailures++;
        }

        return _lastDurationMilliseconds;
    }

    /// <summary>Marks an explicitly disabled operation.</summary>
    public void Disable()
    {
        _state = "disabled";
        _nextExpectedAt = null;
    }

    /// <summary>Records a permanently failed work item.</summary>
    public void RecordTerminalFailure()
    {
        _terminalFailures++;
    }

    /// <summary>Copies progress without exposing mutable state.</summary>
    /// <returns>The current immutable progress.</returns>
    public WorkerTelemetrySnapshot Capture()
    {

        return new WorkerTelemetrySnapshot
        {
            State = _state,
            StartedAt = _startedAt,
            NextExpectedAt = _nextExpectedAt,
            LastCompletedAt = _lastCompletedAt,
            ConsecutiveFailures = _consecutiveFailures,
            Successes = _successes,
            Failures = _failures,
            TerminalFailures = _terminalFailures,
            LastDurationMilliseconds = _lastDurationMilliseconds
        };
    }
}
