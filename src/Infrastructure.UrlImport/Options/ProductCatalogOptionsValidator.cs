using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;

/// <summary>Checks that optional catalog configuration uses only a bounded public index identifier.</summary>
public class ProductCatalogOptionsValidator : IValidateOptions<ProductCatalogOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(
        string? name,
        ProductCatalogOptions options)
    {

        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        return options.IndexKey.StartsWith(
            "key_",
            StringComparison.Ordinal) && options.IndexKey.Length is > 4 and <= 100 && options.IndexKey.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("UrlImportCatalog:IndexKey must identify the public storefront search index.");
    }
}
