namespace JennGllg.Fr.MonKado.Back.Worker.Options;
/// <summary>
/// Represents authentication email options.
/// </summary>

public class AuthenticationEmailOptions
{
    private static readonly TimeSpan _defaultDeliveryLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _defaultPollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _defaultFailureRetryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _defaultFirstRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _defaultSecondRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _defaultThirdRetryDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan _defaultFourthRetryDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan _defaultSubsequentRetryDelay = TimeSpan.FromHours(6);
    private static readonly TimeSpan _defaultSlowRetryDelay = TimeSpan.FromHours(6);
    private static readonly TimeSpan _defaultMaximumRetryDelay = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets the default processed email retention in days.
    /// </summary>
    public const int DefaultProcessedRetentionDays = 30;
    /// <summary>
    /// Identifies section name.
    /// </summary>
    public const string SectionName = "AuthenticationEmail";
    /// <summary>
    /// Identifies disabled provider.
    /// </summary>
    public const string DisabledProvider = "Disabled";
    /// <summary>
    /// Identifies gmail provider.
    /// </summary>
    public const string GmailProvider = "Gmail";
    /// <summary>
    /// Gets provider.
    /// </summary>

    public string Provider { get; init; } = DisabledProvider;
    /// <summary>
    /// Gets frontend origin.
    /// </summary>

    public string? FrontendOrigin
    {
        get; init;
    }
    /// <summary>
    /// Gets the age in days after which processed authentication emails become eligible for deletion.
    /// </summary>
    public int ProcessedRetentionDays { get; init; } = DefaultProcessedRetentionDays;

    /// <summary>
    /// Gets the maximum number of e-mails delivered in one worker cycle.
    /// </summary>
    public int DeliveryBatchSize { get; init; } = 20;

    /// <summary>
    /// Gets the duration of one outbox claim.
    /// </summary>
    public TimeSpan DeliveryLeaseDuration { get; init; } = _defaultDeliveryLeaseDuration;

    /// <summary>
    /// Gets the delay between successful delivery cycles.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = _defaultPollInterval;

    /// <summary>
    /// Gets the delay after an unexpected worker failure.
    /// </summary>
    public TimeSpan FailureRetryInterval { get; init; } = _defaultFailureRetryInterval;

    /// <summary>
    /// Gets the maximum number of provider calls for one outbox message.
    /// </summary>
    public int MaximumDeliveryAttempts { get; init; } = 10;

    /// <summary>
    /// Gets the delay after the first transient delivery failure.
    /// </summary>
    public TimeSpan FirstRetryDelay { get; init; } = _defaultFirstRetryDelay;

    /// <summary>
    /// Gets the delay after the second transient delivery failure.
    /// </summary>
    public TimeSpan SecondRetryDelay { get; init; } = _defaultSecondRetryDelay;

    /// <summary>
    /// Gets the delay after the third transient delivery failure.
    /// </summary>
    public TimeSpan ThirdRetryDelay { get; init; } = _defaultThirdRetryDelay;

    /// <summary>
    /// Gets the delay after the fourth transient delivery failure.
    /// </summary>
    public TimeSpan FourthRetryDelay { get; init; } = _defaultFourthRetryDelay;

    /// <summary>
    /// Gets the delay after subsequent transient delivery failures.
    /// </summary>
    public TimeSpan SubsequentRetryDelay { get; init; } = _defaultSubsequentRetryDelay;

    /// <summary>
    /// Gets the delay after a non-transient provider rejection.
    /// </summary>
    public TimeSpan SlowRetryDelay { get; init; } = _defaultSlowRetryDelay;

    /// <summary>
    /// Gets the maximum accepted provider retry delay.
    /// </summary>
    public TimeSpan MaximumRetryDelay { get; init; } = _defaultMaximumRetryDelay;
    /// <summary>
    /// Gets is enabled.
    /// </summary>

    public bool IsEnabled => Provider.Equals(
        GmailProvider,
        StringComparison.Ordinal);
}
