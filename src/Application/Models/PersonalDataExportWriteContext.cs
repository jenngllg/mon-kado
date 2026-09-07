using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Provides bounded archive output and a private disk-backed image-reference spool.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportWriteContext
{
    /// <summary>Gets the size-limited non-seekable archive destination.</summary>
    public Stream Archive { get; init; } = Stream.Null;
    /// <summary>Gets the seekable private manifest stream, removed after the attempt.</summary>
    public Stream ImageManifest { get; init; } = Stream.Null;
}
