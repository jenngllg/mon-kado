using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;

/// <summary>Defines the versioned private local monitoring contract.</summary>
[ExcludeFromCodeCoverage]
public class ApplicationTelemetrySnapshot
{
    /// <summary>Gets the contract version.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Gets the api or worker service name.</summary>
    public string Service { get; init; } = string.Empty;
    /// <summary>Gets the process lifetime identifier used to detect counter resets.</summary>
    public string BootId { get; init; } = string.Empty;
    /// <summary>Gets the UTC snapshot creation time.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
    /// <summary>Gets cumulative HTTP measurements.</summary>
    public HttpTelemetrySnapshot Http { get; init; } = new();
    /// <summary>Gets progress by bounded operation name.</summary>
    public IReadOnlyDictionary<string, WorkerTelemetrySnapshot> Jobs { get; init; } = new Dictionary<string, WorkerTelemetrySnapshot>();
}
