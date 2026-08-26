namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

/// <summary>
/// Configures persistent anonymous browser sessions.
/// </summary>
public class GuestSessionOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "GuestSessions";

    /// <summary>Gets the absolute guest-session lifetime.</summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromDays(180);
}
