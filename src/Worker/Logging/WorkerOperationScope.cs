using System.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Worker.Logging;

/// <summary>
/// Owns the tracing activity and logging scope for one worker operation.
/// </summary>
/// <param name="activity">The tracing activity owned by the operation.</param>
/// <param name="loggingScope">The logging scope owned by the operation.</param>
public sealed class WorkerOperationScope(
    Activity activity,
    IDisposable? loggingScope) : IDisposable
{
    private readonly Activity _activity = activity;
    private readonly IDisposable? _loggingScope = loggingScope;

    /// <summary>
    /// Disposes the logging scope and tracing activity.
    /// </summary>
    public void Dispose()
    {
        _loggingScope?.Dispose();
        _activity.Dispose();
    }
}
