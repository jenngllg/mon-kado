using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;

/// <summary>Contains unambiguous merchant suggestions before image retrieval.</summary>
[ExcludeFromCodeCoverage]
public class MerchantMetadata
{
    /// <summary>Gets the sanitized suggested name.</summary>
    public string? Name
    {
        get; init;
    }
    /// <summary>Gets the validated euro price.</summary>
    public decimal? Price
    {
        get; init;
    }
    /// <summary>Gets the candidate remote image URL.</summary>
    public Uri? ImageUrl
    {
        get; init;
    }
    /// <summary>Gets whether an unsupported currency prevented price extraction.</summary>
    public bool CurrencyUnsupported
    {
        get; init;
    }
}
