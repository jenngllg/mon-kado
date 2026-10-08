using AngleSharp.Dom;
using AngleSharp.Html.Parser;

using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

/// <summary>Reads structured metadata and product markup without executing scripts.</summary>
public class MerchantMetadataExtractor : IMerchantMetadataExtractor
{
    private static readonly decimal _maximumPrice = 99_999_999.99m;
    private const string AmazonPriceContainers = "#corePriceDisplay_desktop_feature_div, #corePriceDisplay_mobile_feature_div, #corePrice_desktop, #corePrice_feature_div, #corePrice_mobile_feature_div, #apex_desktop, #apex_mobile, #buybox, #mobile_buybox";
    private const string EuropeanAmountPattern = @"^(?:[0-9]+|[0-9]{1,3}(?:[ .][0-9]{3})+)(?:,[0-9]{1,2})?$";
    private const string InternationalAmountPattern = @"^(?:[0-9]+|[0-9]{1,3}(?:[ ,][0-9]{3})+)\.[0-9]{1,2}$";
    private static readonly string[] _amazonDomains =
    [
        "amazon.fr",
        "amazon.com",
        "amazon.co.uk",
        "amazon.de",
        "amazon.it",
        "amazon.es",
        "amazon.nl",
        "amazon.com.be",
        "amazon.ie",
        "amazon.pl",
        "amazon.se",
        "amazon.com.tr",
        "amazon.ca",
        "amazon.com.mx",
        "amazon.com.br",
        "amazon.in",
        "amazon.co.jp",
        "amazon.com.au",
        "amazon.sg",
        "amazon.ae",
        "amazon.sa",
        "amazon.eg",
        "amazon.co.za"
    ];
    /// <inheritdoc/>
    public MerchantMetadata Extract(ImportDocument document)
    {
        var encoding = GetEncoding(document.Charset);
        using var html = new HtmlParser().ParseDocument(encoding.GetString(document.Content));

        if (IsAmazonHost(document.Url.Host))
            return ExtractAmazon(
                html,
                document.Url);
        var products = html
            .QuerySelectorAll("script[type='application/ld+json']")
            .SelectMany(script => ReadProducts(script.TextContent))
            .ToArray();

        if (products.Length == 0)
            products = ReadMicrodataProducts(html);

        if (products.Length == 0 && IsBrowserChallenge(html))
            return new MerchantMetadata();

        if (products.Length == 0 && (document.Url.Host.Equals("fnac.com", StringComparison.OrdinalIgnoreCase)
                || document.Url.Host.EndsWith(".fnac.com", StringComparison.OrdinalIgnoreCase)))
            return ExtractFnac(
                html,
                document.Url);
        var matchingProducts = products
            .Where(product => MatchesProductUrl(
                product,
                document.Url))
            .ToArray();

        // Multiple products can describe variants or unrelated recommendations; do not guess.
        if (products.Length > 1 && matchingProducts.Length != 1)
            return new MerchantMetadata();
        var product = matchingProducts.Length == 1 ? matchingProducts[0] : products.SingleOrDefault();
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
        var prices = ReadOffers(
                product,
                document.Url)
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
            ImageUrl = ResolveProductImage(
                html,
                document.Url,
                image),
            CurrencyUnsupported = currencyUnsupported
        };
    }

    /// <summary>Rejects explicit browser challenges instead of importing a storefront logo or error title.</summary>
    /// <param name="html">The passive merchant document.</param>
    /// <returns>Whether the document requires an interactive browser challenge.</returns>
    private static bool IsBrowserChallenge(IDocument html)
    {

        return html.QuerySelector("#challenge-form, #cf-challenge-running, #px-captcha, #sec-if-cpt-container, script[src*='/cdn-cgi/challenge-platform/']") is not null
            || html.Scripts.Any(script => script.TextContent.Contains(
                "/cdn-cgi/challenge-platform/",
                StringComparison.Ordinal));
    }

    /// <summary>Identifies the main product by its declared page or offer URL, retaining ambiguous variants.</summary>
    /// <param name="product">The candidate product.</param>
    /// <param name="documentUrl">The final validated page URL.</param>
    /// <returns>Whether a declared product URL identifies the current page.</returns>
    private static bool MatchesProductUrl(
        JsonElement product,
        Uri documentUrl)
    {
        var urls = ReadOfferNodes(product)
            .Select(offer => ReadString(
                offer,
                "url"))
            .Append(ReadString(
                product,
                "url"));

        return urls.Any(candidate => MatchesDocumentUrl(
            candidate,
            documentUrl));
    }

    /// <summary>Matches a declared product or offer URL without treating another variant as the current page.</summary>
    /// <param name="candidate">The optional declared URL.</param>
    /// <param name="documentUrl">The final validated page URL.</param>
    /// <returns>Whether the declaration identifies the current document.</returns>
    private static bool MatchesDocumentUrl(
        string? candidate,
        Uri documentUrl)
    {

        return !string.IsNullOrWhiteSpace(candidate) && Uri.TryCreate(
                documentUrl,
                candidate,
                out var url) && string.Equals(
                url.GetLeftPart(UriPartial.Path),
                documentUrl.GetLeftPart(UriPartial.Path),
                StringComparison.Ordinal) && (url.Query.Length == 0 || url.Query == documentUrl.Query);
    }

    /// <summary>Reads passive schema.org microdata when the page does not publish JSON-LD products.</summary>
    /// <param name="html">The bounded merchant document.</param>
    /// <returns>Product nodes compatible with the common ambiguity checks.</returns>
    private static JsonElement[] ReadMicrodataProducts(IDocument html)
    {

        return html
            .QuerySelectorAll("[itemscope][itemtype~='https://schema.org/Product'], [itemscope][itemtype~='http://schema.org/Product']")
            .Select(product => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["name"] = ReadMicrodataValue(
                    product,
                    "name"),
                ["url"] = ReadMicrodataValue(
                    product,
                    "url"),
                ["image"] = ReadMicrodataValue(
                    product,
                    "image"),
                ["sku"] = ReadMicrodataValue(
                    product,
                    "sku") ?? NormalizeText(product.GetAttribute("data-product-sku")),
                ["offers"] = product
                    .QuerySelectorAll("[itemprop~='offers'][itemscope][itemtype~='https://schema.org/Offer'], [itemprop~='offers'][itemscope][itemtype~='http://schema.org/Offer']")
                    // A queried descendant necessarily has an element parent inside this product.
                    .Where(offer => offer.ParentElement!.Closest("[itemscope]") == product)
                    .Select(offer => new Dictionary<string, string?>
                    {
                        ["price"] = ReadMicrodataValue(
                            offer,
                            "price"),
                        ["priceCurrency"] = ReadMicrodataValue(
                            offer,
                            "priceCurrency"),
                        ["url"] = ReadMicrodataValue(
                            offer,
                            "url"),
                        ["sku"] = ReadMicrodataValue(
                            offer,
                            "sku")
                    })
                    .ToArray()
            }))
            .ToArray();
    }

    /// <summary>Reads a unique scalar owned by the scope, excluding nested brands, reviews and offers.</summary>
    /// <param name="scope">The owning microdata scope.</param>
    /// <param name="property">The schema property token.</param>
    /// <returns>The unambiguous scalar, or no suggestion.</returns>
    private static string? ReadMicrodataValue(
        IElement scope,
        string property)
    {
        var values = scope
            .QuerySelectorAll($"[itemprop~='{property}']:not([itemscope])")
            .Where(element => element.Closest("[itemscope]") == scope)
            .Select(element => NormalizeText(element.GetAttribute("content")
                ?? element.GetAttribute("href")
                ?? element.GetAttribute("src")
                ?? element.TextContent))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return values.Length == 1 ? values[0] : null;
    }

    /// <summary>Reads the main product on the newer FNAC storefront without importing other offers.</summary>
    /// <param name="html">The passive merchant document.</param>
    /// <param name="url">The final validated document URL.</param>
    /// <returns>Only unique primary product suggestions.</returns>
    private static MerchantMetadata ExtractFnac(
        IDocument html,
        Uri url)
    {
        var names = html
            .QuerySelectorAll("h1[data-automation-id='pdp-productInformation-title']")
            .Select(element => NormalizeText(element.TextContent))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (names.Length != 1)
            return new MerchantMetadata();
        var images = html
            .QuerySelectorAll("[aria-label='Product images'] button[aria-label='Open image 1 in fullscreen'] img[alt][src]")
            .Where(element => NormalizeText(element.GetAttribute("alt")) == names[0])
            .Select(element => element.GetAttribute("src"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var prices = html
            .QuerySelectorAll("[data-automation-id='pdp-buyBox-desktop'], [data-automation-id='pdp-buyBox-mobile']")
            .SelectMany(box => box.QuerySelectorAll(":scope > div:first-child > [class*='PricingUI-'][class*='__container']:first-child [class*='__pricingLabelMain']"))
            .Select(element => NormalizeText(element.TextContent))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unsupported = prices.Any(price => !HasEuroCurrency(price));
        var amounts = prices
            .Select(ParseAmazonPrice)
            .Distinct()
            .ToArray();

        return new MerchantMetadata
        {
            Name = CleanName(names[0]),
            ImageUrl = ResolveProductImage(
                html,
                url,
                images.Length == 1 ? images[0] : null),
            Price = !unsupported && amounts.Length == 1 ? amounts[0] : null,
            CurrencyUnsupported = unsupported
        };
    }

    /// <summary>Resolves relative image identifiers through an exactly matching visible image when available.</summary>
    /// <param name="html">The passive merchant document.</param>
    /// <param name="documentUrl">The final validated page URL.</param>
    /// <param name="image">The declared product image.</param>
    /// <returns>At most one image candidate for the existing safe downloader.</returns>
    private static Uri? ResolveProductImage(
        IDocument html,
        Uri documentUrl,
        string? image)
    {

        if (string.IsNullOrWhiteSpace(image) || !Uri.TryCreate(
            documentUrl,
            image,
            out var resolved))
            return null;

        if (Uri.IsWellFormedUriString(
            image,
            UriKind.Absolute))
            return resolved;
        var visibleImages = html
            .QuerySelectorAll("img[alt][src]")
            .Where(element => element.GetAttribute("alt") == image)
            .Select(element => Uri.TryCreate(
                documentUrl,
                element.GetAttribute("src"),
                out var source) ? source : null)
            .Where(source => source?.Scheme is "http" or "https")
            .Distinct()
            .ToArray();

        return visibleImages.Length == 1 ? visibleImages[0] : resolved;
    }

    /// <summary>Recognizes marketplace hosts after the existing transport has validated redirects.</summary>
    /// <param name="host">The final document host.</param>
    /// <returns>Whether the document belongs to a supported Amazon marketplace.</returns>
    private static bool IsAmazonHost(string host)
    {

        return _amazonDomains.Any(domain => host.Equals(
                domain,
                StringComparison.OrdinalIgnoreCase) || host.EndsWith(
                "." + domain,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads the selected Amazon product rather than site titles, recommendations or challenges.</summary>
    /// <param name="html">The passive product document.</param>
    /// <param name="url">The final validated document URL.</param>
    /// <returns>Only unambiguous suggestions for the main product.</returns>
    private static MerchantMetadata ExtractAmazon(
        IDocument html,
        Uri url)
    {
        var names = html
            .QuerySelectorAll("#productTitle, #ebooksProductTitle")
            .Select(element => NormalizeText(element.TextContent))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (names.Length != 1 || html.QuerySelector("#captchacharacters, form[action*='validateCaptcha']") is not null)
            return new MerchantMetadata();
        var image = html.QuerySelector("#landingImage, #imgBlkFront, #ebooksImgBlkFront");
        string?[] imageCandidates =
        [
            image?.GetAttribute("data-old-hires"),
            image?.GetAttribute("data-a-hires"),
            image?.GetAttribute("src")
        ];
        var imageUrl = imageCandidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => Uri.TryCreate(
                url,
                candidate,
                out var resolved) ? resolved : null)
            .FirstOrDefault(candidate => candidate?.Scheme is "http" or "https");
        var price = ReadAmazonPrice(html);

        return new MerchantMetadata
        {
            Name = CleanName(names[0]),
            ImageUrl = imageUrl,
            Price = price.Amount,
            CurrencyUnsupported = price.CurrencyUnsupported
        };
    }

    /// <summary>Reads only primary offer prices and refuses conflicting or non-euro amounts.</summary>
    /// <param name="html">The passive product document.</param>
    /// <returns>The exact euro amount and unsupported-currency indicator.</returns>
    private static (decimal? Amount, bool CurrencyUnsupported) ReadAmazonPrice(IDocument html)
    {
        var texts = html
            .QuerySelectorAll(AmazonPriceContainers)
            .SelectMany(container => container.QuerySelectorAll(".priceToPay, .apex-pricetopay-value"))
            .Select(ReadAmazonPriceText)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (texts.Length == 0)
            texts = html
                .QuerySelectorAll(AmazonPriceContainers)
                .SelectMany(container => container.QuerySelectorAll("#apex-pricetopay-accessibility-label"))
                .Select(element => NormalizeText(element.TextContent))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        if (texts.Length == 0)
            texts = html
                .QuerySelectorAll("#priceblock_ourprice, #priceblock_dealprice, #priceblock_saleprice")
                .Select(element => NormalizeText(element.TextContent))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        var unsupported = texts.Any(text => !HasEuroCurrency(text));
        var amounts = texts
            .Select(ParseAmazonPrice)
            .Distinct()
            .ToArray();

        return (!unsupported && amounts.Length == 1 ? amounts[0] : null, unsupported);
    }

    /// <summary>Uses the accessible amount or explicit whole/fraction parts, never duplicate hidden text.</summary>
    /// <param name="element">The selected primary price element.</param>
    /// <returns>One normalized price representation.</returns>
    private static string? ReadAmazonPriceText(IElement element)
    {
        var accessible = NormalizeText(element.QuerySelector(".a-offscreen")?.TextContent);

        if (accessible is not null)
            return accessible;
        var whole = NormalizeText(element.QuerySelector(".a-price-whole")?.TextContent);
        var fraction = NormalizeText(element.QuerySelector(".a-price-fraction")?.TextContent);
        var symbol = NormalizeText(element.QuerySelector(".a-price-symbol")?.TextContent);

        if (whole is not null && fraction is not null && symbol is not null)
        {
            var integer = whole
                .TrimEnd(
                    '.',
                    ',')
                .Replace(
                    ",",
                    ".");

            return $"{integer},{fraction} {symbol}";
        }

        return NormalizeText(element.TextContent);
    }

    /// <summary>Requires an explicit euro marker rather than inferring currency from the host.</summary>
    /// <param name="text">The normalized offer text.</param>
    /// <returns>Whether the amount explicitly declares euros.</returns>
    private static bool HasEuroCurrency(string text)
    {

        return text.Contains('€') || text.Contains(
                "EUR",
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Accepts localized exact amounts without rounding, ranges or currency conversion.</summary>
    /// <param name="text">The normalized primary offer text.</param>
    /// <returns>The validated euro amount, or no suggestion.</returns>
    private static decimal? ParseAmazonPrice(string text)
    {

        if (!HasEuroCurrency(text))
            return null;
        var amount = text
            .Replace(
                "€",
                "",
                StringComparison.Ordinal)
            .Replace(
                "EUR",
                "",
                StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (Regex.IsMatch(
            amount,
            EuropeanAmountPattern,
            RegexOptions.NonBacktracking | RegexOptions.CultureInvariant))
            return ParsePrice(amount
                .Replace(
                    " ",
                    "")
                .Replace(
                    ".",
                    "")
                .Replace(
                    ",",
                    "."));

        if (Regex.IsMatch(
            amount,
            InternationalAmountPattern,
            RegexOptions.NonBacktracking | RegexOptions.CultureInvariant))
            return ParsePrice(amount
                .Replace(
                    " ",
                    "")
                .Replace(
                    ",",
                    ""));

        return null;
    }

    /// <summary>Collapses layout whitespace without executing markup or changing meaningful text.</summary>
    /// <param name="text">The optional element text.</param>
    /// <returns>The trimmed, single-spaced text or no value.</returns>
    private static string? NormalizeText(string? text)
    {

        if (string.IsNullOrWhiteSpace(text))
            return null;

        return string.Join(
            " ",
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
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
            using var document = JsonDocument.Parse(EscapeJsonStringControls(json));

            return FindProducts(document.RootElement)
                .Select(product => product.Clone())
                .ToArray();
        }
        catch (JsonException)
        {

            return [];
        }
    }

    /// <summary>Escapes raw layout controls inside merchant JSON strings without repairing other invalid syntax.</summary>
    /// <param name="json">The bounded inline JSON-LD text.</param>
    /// <returns>The same data with valid JSON string control escaping.</returns>
    private static string EscapeJsonStringControls(string json)
    {
        var result = new StringBuilder(json.Length);
        var insideString = false;
        var escaped = false;
        foreach (var character in json)
        {

            if (insideString && !escaped && character < ' ')
                result.Append(JsonEncodedText.Encode(character.ToString()).ToString());
            else
                result.Append(character);

            if (character == '"' && !escaped)
                insideString = !insideString;
            escaped = insideString && character == '\\' && !escaped;
        }

        return result.ToString();
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
    /// <param name="documentUrl">The current variant URL.</param>
    /// <returns>All offer amounts and currencies.</returns>
    private static IEnumerable<(string? Amount, string? Currency)> ReadOffers(
        JsonElement product,
        Uri documentUrl)
    {
        var offers = ReadOfferNodes(product);
        var matchingOffers = offers
            .Where(offer => MatchesDocumentUrl(
                ReadString(
                    offer,
                    "url"),
                documentUrl))
            .ToArray();
        var sku = ReadString(
            product,
            "sku");

        if (matchingOffers.Length == 0 && !string.IsNullOrWhiteSpace(sku))
            matchingOffers = offers
                .Where(offer => ReadString(
                    offer,
                    "sku") == sku)
                .ToArray();

        return (matchingOffers.Length > 0 ? matchingOffers : offers)
            .Select(offer => (ReadString(
                offer,
                "price"), ReadString(
                offer,
                "priceCurrency")));
    }

    /// <summary>Reads declared offers without interpreting recommendations or aggregate price ranges.</summary>
    /// <param name="product">The optional product.</param>
    /// <returns>The local offer nodes.</returns>
    private static JsonElement[] ReadOfferNodes(JsonElement product)
    {

        if (product.ValueKind != JsonValueKind.Object || !product.TryGetProperty(
            "offers",
            out var offers))
            return [];
        var nodes = offers.ValueKind == JsonValueKind.Array ? offers
            .EnumerateArray()
            .ToArray() : [offers];

        return nodes;
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
