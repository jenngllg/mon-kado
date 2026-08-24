namespace JennGllg.Fr.MonKado.Back.Worker.Options;
/// <summary>
/// Represents authentication email options.
/// </summary>

public class AuthenticationEmailOptions
{
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
    /// Gets is enabled.
    /// </summary>

    public bool IsEnabled => Provider.Equals(
        GmailProvider,
        StringComparison.Ordinal);
}
