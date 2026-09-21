using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;

/// <summary>Configures bounded private process snapshots.</summary>
[ExcludeFromCodeCoverage]
public class ObservabilityOptions
{
    /// <summary>Gets whether private filesystem snapshots are enabled.</summary>
    public bool Enabled
    {
        get; init;
    }
    /// <summary>Gets the process service name, assigned by its composition root.</summary>
    public string Service { get; init; } = "api";
    /// <summary>Gets the published source revision or the local development marker.</summary>
    public string Version { get; init; } = "local";
    /// <summary>Gets the dedicated writable snapshot directory.</summary>
    public string Directory { get; init; } = "/var/lib/monkado-observability";
    /// <summary>Gets the snapshot interval.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
}
