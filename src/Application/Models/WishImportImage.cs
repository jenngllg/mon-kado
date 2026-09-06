using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains normalized image bytes for a client-side preview.</summary>
[ExcludeFromCodeCoverage]
public class WishImportImage
{
    /// <summary>Gets the Base64-encoded WebP content.</summary>
    public string? ContentBase64
    {
        get; init;
    }
    /// <summary>Gets the image media type.</summary>
    public string? ContentType
    {
        get; init;
    }
}
