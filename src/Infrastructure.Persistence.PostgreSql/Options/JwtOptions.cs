using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

/// <summary>
/// Represents JWT authentication options.
/// </summary>
[ExcludeFromCodeCoverage]
public class JwtOptions
{
    /// <summary>
    /// Identifies the JWT configuration section.
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// Gets the token issuer.
    /// </summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>
    /// Gets the token audience.
    /// </summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded signing key.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;
}
