using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;

/// <summary>Contains cumulative counters and disjoint latency buckets for one process.</summary>
[ExcludeFromCodeCoverage]
public class HttpTelemetrySnapshot
{
    /// <summary>Gets the number of observed requests.</summary>
    public long Requests
    {
        get; init;
    }
    /// <summary>Gets the number of server error responses.</summary>
    public long ServerErrors
    {
        get; init;
    }
    /// <summary>Gets the number of throttled requests.</summary>
    public long Throttled
    {
        get; init;
    }
    /// <summary>Gets the number of abandoned requests.</summary>
    public long Abandoned
    {
        get; init;
    }
    /// <summary>Gets disjoint buckets ending at 50, 100, 250, 500, 1000, 2000, 5000, 10000 milliseconds and infinity.</summary>
    public IReadOnlyList<long> DurationBuckets { get; init; } = [];
}
