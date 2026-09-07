using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Options;

/// <summary>Defines stable generation, throttling and retention limits.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "PersonalDataExports";
    /// <summary>Gets the archive lifetime measured from confirmed publication.</summary>
    public TimeSpan ArchiveLifetime { get; init; } = TimeSpan.FromHours(24);
    /// <summary>Gets the durable rolling quota window.</summary>
    public TimeSpan RequestWindow { get; init; } = TimeSpan.FromHours(24);
    /// <summary>Gets the maximum new requests per member within the quota window.</summary>
    public int MaximumRequests { get; init; } = 3;
    /// <summary>Gets the maximum generation attempts.</summary>
    public int MaximumAttempts { get; init; } = 5;
    /// <summary>Gets the complete archive size limit, including ZIP metadata.</summary>
    public long MaximumArchiveBytes { get; init; } = 1024L * 1024 * 1024;
    /// <summary>Gets the deadline of each generation attempt.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>Gets the renewable worker lease duration.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Gets the renewal interval, shorter than the lease duration.</summary>
    public TimeSpan LeaseRenewalInterval { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets the successful worker-cycle interval.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets the interval after a failed worker cycle.</summary>
    public TimeSpan FailureInterval { get; init; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets the bounded number of cleanup candidates per cycle.</summary>
    public int CleanupBatchSize { get; init; } = 100;
    /// <summary>Gets the minimum abandoned-attempt age before filesystem reconciliation.</summary>
    public TimeSpan TemporaryGracePeriod { get; init; } = TimeSpan.FromHours(1);
    /// <summary>Gets the delays following successive generation failures.</summary>
    public TimeSpan[] RetryDelays
    {
        get; init;
    } = [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1)
    ];
}
