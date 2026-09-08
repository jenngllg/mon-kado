namespace JennGllg.Fr.MonKado.Back.Application.Options;

/// <summary>Configures bounded erasure notification and retention processing.</summary>
public class AccountErasureProcessingOptions
{
    /// <summary>Identifies the configuration section.</summary>
    public const string SectionName = "AccountErasureProcessing";
    /// <summary>Gets the maximum rows processed per operation.</summary>
    public int BatchSize { get; init; } = 20;
    /// <summary>Gets the maximum number of provider attempts.</summary>
    public int MaximumAttempts { get; init; } = 10;
    /// <summary>Gets the maximum delivery lease.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Gets the successful cycle interval.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets the failed cycle interval.</summary>
    public TimeSpan FailureInterval { get; init; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets increasing retry delays, repeating the last delay when necessary.</summary>
    public TimeSpan[] RetryDelays
    {
        get; init;
    } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6)
    ];
    /// <summary>Gets the maximum provider-requested retry delay.</summary>
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromHours(24);
}
