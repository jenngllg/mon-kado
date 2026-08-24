using Microsoft.Extensions.Logging;

using System.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Worker.Logging;

/// <summary>
/// Creates the mandatory correlation and trace scope for background operations.
/// </summary>
public static class WorkerLogScope
{
    /// <summary>
    /// Starts a correlated worker operation.
    /// </summary>
    /// <param name="logger">The operation logger.</param>
    /// <param name="operation">The stable operation name.</param>
    /// <returns>The scope handle to dispose after the operation.</returns>
    public static WorkerOperationScope Begin(
        ILogger logger,
        string operation)
    {
        var activity = new Activity(operation)
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        var loggingScope = logger.BeginScope(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["CorrelationId"] = Guid.CreateVersion7().ToString("D"),
            ["TraceId"] = activity.TraceId.ToString(),
            ["Operation"] = operation
        });

        return new WorkerOperationScope(
            activity,
            loggingScope);
    }
}
