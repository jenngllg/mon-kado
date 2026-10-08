namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;

/// <summary>Configures an optional anonymous storefront search index, never an administrative API key.</summary>
public class ProductCatalogOptions
{
    /// <summary>Gets the configuration section.</summary>
    public const string SectionName = "UrlImportCatalog";
    /// <summary>Gets whether the public Sephora France catalog is enabled.</summary>
    public bool Enabled
    {
        get; init;
    }
    /// <summary>Gets the public search index identifier published in Sephora's frontend.</summary>
    public string IndexKey { get; init; } = string.Empty;
}
