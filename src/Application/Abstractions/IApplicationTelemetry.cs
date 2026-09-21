using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Records bounded operational measurements without request or member content.</summary>
public interface IApplicationTelemetry
{
    /// <summary>Records one completed or abandoned HTTP request.</summary>
    /// <param name="statusCode">The final HTTP status.</param>
    /// <param name="elapsed">The monotonic elapsed duration.</param>
    /// <param name="abandoned">Whether the request was abandoned.</param>
    void RecordHttp(
        int statusCode,
        TimeSpan elapsed,
        bool abandoned);

    /// <summary>Marks the beginning of actual processing, not a process heartbeat.</summary>
    /// <param name="operation">The registered operation.</param>
    void BeginCycle(WorkerOperation operation);

    /// <summary>Completes a cycle and declares when processing is next expected.</summary>
    /// <param name="operation">The registered operation.</param>
    /// <param name="nextDelay">The configured delay before the next cycle.</param>
    /// <param name="successful">Whether processing completed successfully.</param>
    void CompleteCycle(
        WorkerOperation operation,
        TimeSpan nextDelay,
        bool successful);

    /// <summary>Explicitly disables a configured background operation.</summary>
    /// <param name="operation">The registered operation.</param>
    void Disable(WorkerOperation operation);

    /// <summary>Records a permanently failed work item without its identity or content.</summary>
    /// <param name="operation">The registered operation.</param>
    void RecordTerminalFailure(WorkerOperation operation);
}
