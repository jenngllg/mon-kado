namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

/// <summary>Configures account deletion confirmation lifetime and durable request throttling.</summary>
public class MemberAccountDeletionOptions
{
    /// <summary>Gets the absolute confirmation lifetime.</summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(30);
    /// <summary>Gets the rolling request quota window.</summary>
    public TimeSpan RequestWindow { get; init; } = TimeSpan.FromHours(1);
    /// <summary>Gets the maximum requests per member in the rolling window.</summary>
    public int MaximumRequests { get; init; } = 3;
}
