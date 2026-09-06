using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;

/// <summary>Contains one bounded remote document and its final URL.</summary>
[ExcludeFromCodeCoverage]
public class ImportDocument
{
    /// <summary>Gets the final validated URL after redirects.</summary>
    public required Uri Url
    {
        get; init;
    }
    /// <summary>Gets the bounded decompressed bytes.</summary>
    public required byte[] Content
    {
        get; init;
    }
    /// <summary>Gets the declared content media type.</summary>
    public string? MediaType
    {
        get; init;
    }
    /// <summary>Gets the optional declared character encoding.</summary>
    public string? Charset
    {
        get; init;
    }
}
