using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Describes one internal integrity-checked image copy without retaining file paths.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportImage
{
    /// <summary>Gets the immutable image identifier authorized by the database snapshot.</summary>
    public Guid ImageId
    {
        get; init;
    }
    /// <summary>Gets the owned wish identifier, or null for the member's profile photo.</summary>
    public Guid? WishId
    {
        get; init;
    }
    /// <summary>Gets the expected normalized image SHA-256 digest, kept only in the private temporary manifest.</summary>
    public byte[]? ContentHash
    {
        get; init;
    }
}
