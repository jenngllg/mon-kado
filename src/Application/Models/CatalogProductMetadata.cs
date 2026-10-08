using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains exact public catalog suggestions without provider SDK types.</summary>
[ExcludeFromCodeCoverage]
public class CatalogProductMetadata
{
    /// <summary>Gets the suggested product name.</summary>
    public string? Name
    {
        get; init;
    }
    /// <summary>Gets the selected variant's euro price.</summary>
    public decimal? Price
    {
        get; init;
    }
    /// <summary>Gets the selected variant's candidate image.</summary>
    public Uri? ImageUrl
    {
        get; init;
    }
    /// <summary>Gets whether the price currency is unsupported.</summary>
    public bool CurrencyUnsupported
    {
        get; init;
    }
}
