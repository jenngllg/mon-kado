using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Transfers ownership of an authorized archive stream to the HTTP response.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportDownload
{
    /// <summary>Gets the generated export identifier.</summary>
    public Guid ExportId
    {
        get; init;
    }
    /// <summary>Gets the caller-owned archive stream.</summary>
    public Stream Content { get; init; } = Stream.Null;
}
