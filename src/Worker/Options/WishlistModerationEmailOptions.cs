namespace JennGllg.Fr.MonKado.Back.Worker.Options;

/// <summary>Configures moderation notification delivery independently from authentication messages.</summary>
public class WishlistModerationEmailOptions
{
    /// <summary>Identifies the configuration section.</summary>
    public const string SectionName = "WishlistModerationEmail";
    /// <summary>Gets the maximum claims and cleanup rows per cycle.</summary>
    public int BatchSize { get; init; } = 20;
    /// <summary>Gets the maximum provider attempts per event.</summary>
    public int MaximumAttempts { get; init; } = 10;
    /// <summary>Gets the lease duration, also bounding the complete provider operation.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Gets the interval between successful cycles.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets the interval after a worker or PostgreSQL failure.</summary>
    public TimeSpan FailureInterval { get; init; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets the increasing retry delays; the final delay applies to subsequent attempts.</summary>
    public TimeSpan[] RetryDelays
    {
        get; init;
    } = [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6)
    ];
    /// <summary>Gets the upper bound for provider Retry-After.</summary>
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromHours(24);
    /// <summary>Gets the number of days to retain processed outbox records, without purging audit history.</summary>
    public int ProcessedRetentionDays { get; init; } = 30;
}
