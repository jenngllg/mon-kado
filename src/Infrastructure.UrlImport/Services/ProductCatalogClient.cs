using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;

using Microsoft.Extensions.Options;

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

/// <summary>Reads Sephora's anonymous public search index using exact product and variant identities.</summary>
/// <param name="client">The existing DNS-pinned, bounded public HTTP client.</param>
/// <param name="extractor">The existing sanitizer and currency/price validation.</param>
/// <param name="options">The explicitly configured public storefront index.</param>
public partial class ProductCatalogClient(
    IUrlImportClient client,
    IMerchantMetadataExtractor extractor,
    IOptions<ProductCatalogOptions> options) : IProductCatalogClient
{
    private const int MaximumCatalogBytes = 256 * 1024;

    /// <inheritdoc/>
    public bool CanHandle(Uri url)
    {

        return options.Value.Enabled && IsStorefront(url) && ProductPath()
            .IsMatch(url.AbsolutePath);
    }

    /// <inheritdoc/>
    public async Task<CatalogProductMetadata> GetAsync(
        Uri url,
        CancellationToken cancellationToken)
    {

        if (!CanHandle(url))
            return new CatalogProductMetadata();
        var id = ProductPath()
            .Match(url.AbsolutePath)
            .Groups["id"].Value;
        var endpoint = new Uri("https://ac.cnstrc.com/search/" + Uri.EscapeDataString(id) + "?key=" + Uri.EscapeDataString(options.Value.IndexKey) + "&num_results_per_page=3");
        var document = await client.DownloadAsync(
            endpoint,
            MaximumCatalogBytes,
            cancellationToken);

        if (!string.Equals(
            document.MediaType,
            "application/json",
            StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException("The public product catalog response is not JSON.");
        try
        {
            using var json = JsonDocument.Parse(document.Content);

            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty(
                "response",
                out var response) || response.ValueKind != JsonValueKind.Object || !response.TryGetProperty(
                "results",
                out var results) || results.ValueKind != JsonValueKind.Array)
                return new CatalogProductMetadata();
            var candidates = results
                .EnumerateArray()
                .Where(result => result.ValueKind == JsonValueKind.Object)
                .Select(result => SelectVariant(
                    result,
                    id))
                .Where(variant => variant.HasValue)
                .Select(variant => variant.GetValueOrDefault())
                .ToArray();

            if (candidates.Length != 1)
                return new CatalogProductMetadata();

            return Normalize(
                candidates[0],
                url);
        }
        catch (JsonException)
        {

            throw new HttpRequestException("The public product catalog response is invalid.");
        }
    }

    /// <summary>Selects only the requested SKU, or the explicitly declared default of an exact master product.</summary>
    /// <param name="result">One search result, potentially only a fuzzy suggestion.</param>
    /// <param name="id">The exact reference parsed from the original product path.</param>
    /// <returns>The unique selected variant, never a first or cheapest search result.</returns>
    private static JsonElement? SelectVariant(
        JsonElement result,
        string id)
    {

        if (!result.TryGetProperty(
            "data",
            out var data) || data.ValueKind != JsonValueKind.Object)
            return null;
        var selectedId = id;

        if (id.StartsWith('P'))
        {

            if (GetString(
                data,
                "id") != id || !data.TryGetProperty(
                "defaultVariantId",
                out var defaultId) || defaultId.ValueKind != JsonValueKind.Number || !defaultId.TryGetInt64(out var numericId))
                return null;
            selectedId = numericId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var variants = new List<JsonElement>();

        if (IsExactVariant(
            data,
            selectedId))
            variants.Add(data);

        if (result.TryGetProperty(
            "variations",
            out var variations) && variations.ValueKind == JsonValueKind.Array)
            variants.AddRange(variations
                .EnumerateArray()
                .Where(variation => variation.ValueKind == JsonValueKind.Object)
                .Where(variation => variation.TryGetProperty(
                    "data",
                    out _))
                .Select(variation => variation.GetProperty("data"))
                .Where(variant => IsExactVariant(
                    variant,
                    selectedId)));
        var distinct = variants
            .GroupBy(variant => JsonSerializer.Serialize(new
            {
                Name = GetString(
                    variant,
                    "name"),
                Price = variant.TryGetProperty(
                    "price",
                    out var price) ? price.GetRawText() : null,
                Image = GetString(
                    variant,
                    "image_url")
            }))
            .Select(group => group.First())
            .ToArray();

        return distinct.Length == 1 ? distinct[0] : null;
    }

    /// <summary>Requires a matching SKU and a matching first-party product URL, not search ranking.</summary>
    /// <param name="data">The candidate variant.</param>
    /// <param name="id">The exact desired SKU.</param>
    /// <returns>Whether both independent identities match.</returns>
    private static bool IsExactVariant(
        JsonElement data,
        string id)
    {

        if (data.ValueKind != JsonValueKind.Object || GetString(
            data,
            "variation_id") != id || !Uri.TryCreate(
            GetString(
                data,
                "url"),
            UriKind.Absolute,
            out var url) || !IsStorefront(url))
            return false;

        return ProductPath()
            .Match(url.AbsolutePath)
            .Groups["id"].Value == id;
    }

    /// <summary>Reuses existing passive sanitization rather than introducing different product validation.</summary>
    /// <param name="data">The exact variant's public metadata.</param>
    /// <param name="url">The original merchant URL.</param>
    /// <returns>Validated, provider-independent suggestions.</returns>
    private CatalogProductMetadata Normalize(
        JsonElement data,
        Uri url)
    {
        string? image = null;

        if (Uri.TryCreate(
            GetString(
                data,
                "image_url"),
            UriKind.Absolute,
            out var imageUrl) && imageUrl.IdnHost.Equals(
                "media.sephora.eu",
                StringComparison.OrdinalIgnoreCase) && imageUrl.IsDefaultPort && string.IsNullOrEmpty(imageUrl.UserInfo) && imageUrl.Scheme is "http" or "https")
            image = new UriBuilder(imageUrl)
            {
                Scheme = "https",
                Port = -1,
                Query = "scaleWidth=750&scaleHeight=750&scaleMode=fit"
            }.Uri.AbsoluteUri;
        data.TryGetProperty(
            "price",
            out var price);
        var product = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["name"] = GetString(
                data,
                "name"),
            ["url"] = url.AbsoluteUri,
            ["image"] = image,
            ["offers"] = new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["price"] = price.ValueKind == JsonValueKind.Object && price.TryGetProperty(
                    "salesPrice",
                    out var amount) ? amount : (JsonElement?)null,
                ["priceCurrency"] = GetString(
                    price,
                    "currency")
            }
        });
        var metadata = extractor.Extract(new ImportDocument
        {
            Url = url,
            MediaType = "text/html",
            Charset = "utf-8",
            Content = Encoding.UTF8.GetBytes("<script type=\"application/ld+json\">" + product + "</script>")
        });

        return new CatalogProductMetadata
        {
            Name = metadata.Name,
            Price = metadata.Price,
            ImageUrl = metadata.ImageUrl,
            CurrencyUnsupported = metadata.CurrencyUnsupported
        };
    }

    /// <summary>Reads only a string-valued scalar from untrusted public catalog JSON.</summary>
    /// <param name="element">The candidate JSON object.</param>
    /// <param name="name">The expected field.</param>
    /// <returns>The field string, or null for other shapes.</returns>
    private static string? GetString(
        JsonElement element,
        string name)
    {

        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(
            name,
            out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>Limits public catalog lookup to the genuine HTTPS French storefront.</summary>
    /// <param name="url">The original or returned product URL.</param>
    /// <returns>Whether the authority is the expected storefront without credentials.</returns>
    private static bool IsStorefront(Uri url)
    {

        return url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.Port == 443 && string.IsNullOrEmpty(url.UserInfo) && url.IdnHost is "www.sephora.fr" or "sephora.fr";
    }

    /// <summary>Matches canonical product paths, including master products and numeric variants.</summary>
    /// <returns>The bounded storefront path pattern.</returns>
    [GeneratedRegex(@"^/p/(?:[^/]*-)?(?<id>P[0-9]{1,12}|[0-9]{1,12})\.html$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductPath();
}
