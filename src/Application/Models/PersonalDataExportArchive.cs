using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Describes a fully written, unpublished archive attempt.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportArchive
{
    /// <summary>Gets the coherent database snapshot date.</summary>
    public DateTime SnapshotAt
    {
        get; init;
    }
    /// <summary>Gets the complete ZIP length.</summary>
    public long SizeInBytes
    {
        get; init;
    }
}
