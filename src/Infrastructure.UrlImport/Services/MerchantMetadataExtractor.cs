using AngleSharp.Dom;
using AngleSharp.Html.Parser;

using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

/// <summary>Reads JSON-LD product metadata, Open Graph, and the HTML title without executing scripts.</summary>
public class MerchantMetadataExtractor : IMerchantMetadataExtractor
{
    private static readonly decimal _maximumPrice = 99_999_999.99m;
    /// <inheritdoc/>
    public MerchantMetadata Extract(ImportDocument document)
    {
        var encoding = GetEncoding(document.Charset);
        using var html = new HtmlParser().ParseDocument(encoding.GetString(document.Content));
        var products = html
            .QuerySelectorAll("script[type='application/ld+json']")
            .SelectMany(script => ReadProducts(script.TextContent))
            .ToArray();

        // Multiple products can describe variants or unrelated recommendations; do not guess.
        if (products.Length > 1)
            return new MerchantMetadata();
        var product = products.SingleOrDefault();
        var name = CleanName(ReadString(
                product,
                "name")) ?? CleanName(ReadMeta(
                html,
                "og:title")) ?? CleanName(html.Title);
        var image = ReadImage(product);

        if (string.IsNullOrWhiteSpace(image))
            image = ReadMeta(
                html,
                "og:image");
        var prices = ReadOffers(product)
            .ToArray();

        if (prices.All(price => string.IsNullOrWhiteSpace(price.Amount)))
        {
            prices = [(ReadMeta(
                    html,
                    "product:price:amount"), ReadMeta(
                    html,
                    "product:price:currency"))];
        }

        var currencyUnsupported = prices.Any(price => price.Amount is not null && !string.Equals(
                price.Currency,
                "EUR",
                StringComparison.OrdinalIgnoreCase));
        var amounts = prices
            .Select(price => ParsePrice(price.Amount))
            .Distinct()
            .ToArray();
        var amount = !currencyUnsupported && amounts.Length == 1 ? amounts[0] : null;

        return new MerchantMetadata
        {
            Name = name,
            Price = amount,
            ImageUrl = !string.IsNullOrWhiteSpace(image) && Uri.TryCreate(
                document.Url,
                image,
                out var imageUrl) ? imageUrl : null,
            CurrencyUnsupported = currencyUnsupported
        };
    }

    /// <summary>Decodes declared encodings without enabling any external resource loader.</summary>
    /// <param name="charset">The optional HTTP charset.</param>
    /// <returns>The declared encoding or UTF-8 when unavailable.</returns>
    private static Encoding GetEncoding(string? charset)
    {

        if (string.IsNullOrWhiteSpace(charset))
            return Encoding.UTF8;
        try
        {

            return Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {

            return Encoding.UTF8;
        }
    }

    /// <summary>Reads independent JSON-LD products and tolerates invalid merchant markup.</summary>
    /// <param name="json">One JSON-LD script.</param>
    /// <returns>Detached product nodes.</returns>
    private static JsonElement[] ReadProducts(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return FindProducts(document.RootElement)
                .Select(product => product.Clone())
                .ToArray();
        }
        catch (JsonException)
        {

            return [];
        }
    }

    /// <summary>Visits JSON-LD arrays and graphs without resolving remote contexts.</summary>
    /// <param name="node">The current node.</param>
    /// <returns>Product nodes from the local document.</returns>
    private static IEnumerable<JsonElement> FindProducts(JsonElement node)
    {

        if (node.ValueKind == JsonValueKind.Array)
            return node
                .EnumerateArray()
                .SelectMany(FindProducts);

        if (node.ValueKind != JsonValueKind.Object)
            return [];

        if (node.TryGetProperty(
            "@type",
            out var type) && IsProductType(type))
            return [node];

        return node.TryGetProperty(
            "@graph",
            out var graph) ? FindProducts(graph) : [];
    }

    /// <summary>Recognizes Product types, including type arrays and canonical schema URLs.</summary>
    /// <param name="type">The JSON-LD type node.</param>
    /// <returns>Whether the node describes a Product.</returns>
    private static bool IsProductType(JsonElement type)
    {

        if (type.ValueKind == JsonValueKind.Array)
            return type
                .EnumerateArray()
                .Any(IsProductType);

        return type.ValueKind == JsonValueKind.String && type.GetString() is "Product" or "https://schema.org/Product" or "http://schema.org/Product";
    }

    /// <summary>Reads exact offer prices without choosing a variant or a price range.</summary>
    /// <param name="product">The optional product.</param>
    /// <returns>All offer amounts and currencies.</returns>
    private static IEnumerable<(string? Amount, string? Currency)> ReadOffers(JsonElement product)
    {

        if (product.ValueKind != JsonValueKind.Object || !product.TryGetProperty(
            "offers",
            out var offers))
            return [];
        var nodes = offers.ValueKind == JsonValueKind.Array ? offers
            .EnumerateArray()
            .ToArray() : [offers];

        return nodes.Select(offer => (ReadString(
                offer,
                "price"), ReadString(
                offer,
                "priceCurrency")));
    }

    /// <summary>Reads the first declared product image without fetching additional candidates.</summary>
    /// <param name="product">The optional product.</param>
    /// <returns>The candidate image URL.</returns>
    private static string? ReadImage(JsonElement product)
    {

        if (product.ValueKind != JsonValueKind.Object || !product.TryGetProperty(
            "image",
            out var image))
            return null;

        if (image.ValueKind == JsonValueKind.Array)
            image = image
                .EnumerateArray()
                .FirstOrDefault();

        return image.ValueKind == JsonValueKind.String ? image.GetString() : ReadString(
            image,
            "url");
    }

    /// <summary>Reads a scalar without throwing on unexpected merchant types.</summary>
    /// <param name="node">The containing node.</param>
    /// <param name="property">The property name.</param>
    /// <returns>The string or numeric scalar, if present.</returns>
    private static string? ReadString(
        JsonElement node,
        string property)
    {

        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(
            property,
            out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    /// <summary>Rejects conflicting duplicate metadata instead of choosing one value.</summary>
    /// <param name="html">The passive parsed document.</param>
    /// <param name="property">The metadata property.</param>
    /// <returns>The unique nonempty content value.</returns>
    private static string? ReadMeta(
        IDocument html,
        string property)
    {
        var values = html
            .QuerySelectorAll("meta")
            .Where(element => string.Equals(
                element.GetAttribute("property") ?? element.GetAttribute("name"),
                property,
                StringComparison.OrdinalIgnoreCase))
            .Select(element => element.GetAttribute("content")?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return values.Length == 1 ? values[0] : null;
    }

    /// <summary>Accepts only names that already satisfy the creation contract.</summary>
    /// <param name="name">The candidate name.</param>
    /// <returns>The trimmed name, or no suggestion.</returns>
    private static string? CleanName(string? name)
    {

        if (name is null)
            return null;

        return WishTextValidation.IsValidName(name) ? name.Trim() : null;
    }

    /// <summary>Accepts exact positive decimal amounts supported by gift creation.</summary>
    /// <param name="amount">The merchant scalar.</param>
    /// <returns>A valid amount without conversion or rounding.</returns>
    private static decimal? ParsePrice(string? amount)
    {

        return decimal.TryParse(
            amount,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var price) && price > 0 && price <= _maximumPrice && decimal.Round(
            price,
            2) == price ? price : null;
    }
}
