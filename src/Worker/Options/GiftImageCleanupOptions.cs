using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Worker.Options;

/// <summary>
/// Defines obsolete and pending gift-image cleanup settings.
/// </summary>
[ExcludeFromCodeCoverage]
public class GiftImageCleanupOptions
{
    private static readonly TimeSpan _defaultInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _defaultFailureRetryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _defaultLeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _defaultPendingGracePeriod = TimeSpan.FromHours(1);
    private static readonly TimeSpan _defaultMaximumRetryDelay = TimeSpan.FromHours(1);

    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "GiftImageCleanup";

    /// <summary>Gets the maximum work items processed in one cycle.</summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>Gets the delay between successful cycles.</summary>
    public TimeSpan Interval { get; init; } = _defaultInterval;

    /// <summary>Gets the delay after a failed cycle.</summary>
    public TimeSpan FailureRetryInterval { get; init; } = _defaultFailureRetryInterval;

    /// <summary>Gets the duration of one deletion claim.</summary>
    public TimeSpan LeaseDuration { get; init; } = _defaultLeaseDuration;

    /// <summary>Gets the minimum pending-marker age before reconciliation.</summary>
    public TimeSpan PendingGracePeriod { get; init; } = _defaultPendingGracePeriod;

    /// <summary>Gets the maximum exponential deletion retry delay.</summary>
    public TimeSpan MaximumRetryDelay { get; init; } = _defaultMaximumRetryDelay;
}
