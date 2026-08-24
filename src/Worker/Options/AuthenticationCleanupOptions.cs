using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Worker.Options;

/// <summary>
/// Defines the shared schedule for authentication cleanup workers.
/// </summary>
[ExcludeFromCodeCoverage]
public class AuthenticationCleanupOptions
{
    private static readonly TimeSpan _defaultInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan _defaultFailureRetryInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "AuthenticationCleanup";

    /// <summary>
    /// Gets the maximum number of rows deleted in one database command.
    /// </summary>
    public int BatchSize { get; init; } = 500;

    /// <summary>
    /// Gets the delay between successful cleanup cycles.
    /// </summary>
    public TimeSpan Interval { get; init; } = _defaultInterval;

    /// <summary>
    /// Gets the delay after a failed cleanup cycle.
    /// </summary>
    public TimeSpan FailureRetryInterval { get; init; } = _defaultFailureRetryInterval;
}
