using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;

/// <summary>Tracks the deterministic progress of one operation.</summary>
public interface IWorkerProgress
{
    /// <summary>Begins a processing cycle.</summary>
    void Begin();
    /// <summary>Completes a processing cycle.</summary>
    /// <param name="nextDelay">The scheduled delay.</param>
    /// <param name="successful">Whether processing succeeded.</param>
    /// <returns>The elapsed milliseconds.</returns>
    double Complete(
        TimeSpan nextDelay,
        bool successful);
    /// <summary>Disables the operation.</summary>
    void Disable();
    /// <summary>Records a terminal work item failure.</summary>
    void RecordTerminalFailure();
    /// <summary>Captures the operation's progress.</summary>
    /// <returns>An immutable snapshot.</returns>
    WorkerTelemetrySnapshot Capture();
}
