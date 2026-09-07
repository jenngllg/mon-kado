using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Options;

/// <summary>Configures the private API/Worker archive volume.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportStorageOptions
{
    /// <summary>Gets the fully qualified archive root outside publicly served directories.</summary>
    public string? StoragePath
    {
        get; init;
    }
}
