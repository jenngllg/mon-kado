using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Options;

/// <summary>Configures the process-local perimeter quota before authentication.</summary>
[ExcludeFromCodeCoverage]
public class GeneralRateLimitOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "GeneralRateLimit";

    /// <summary>Gets the maximum requests per remote address and window.</summary>
    public int PermitLimit { get; init; } = 300;

    /// <summary>Gets the fixed window duration in seconds.</summary>
    public int WindowSeconds { get; init; } = 60;
}
