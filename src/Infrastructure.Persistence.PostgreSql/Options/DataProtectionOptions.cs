using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
/// <summary>
/// Represents data protection options.
/// </summary>

[ExcludeFromCodeCoverage]
public class DataProtectionOptions
{
    /// <summary>
    /// Identifies section name.
    /// </summary>
    public const string SectionName = "DataProtection";
    /// <summary>
    /// Gets keys path.
    /// </summary>

    public string? KeysPath
    {
        get; init;
    }
}
