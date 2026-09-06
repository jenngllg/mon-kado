using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class MerchantMetadataExtractorTests
{
    private readonly MerchantMetadataExtractor _extractor = new();
    [Fact]
    public void Extract_WhenJsonLdProductIsComplete_PrefersStructuredFields()
    {
        // Arrange
        var document = CreateDocument("""
            <title>Fallback</title><meta property="og:title" content="Other">
            <script type="application/ld+json">
            {"@context":"https://private.invalid/context","@type":"Product","name":" Gift ",
             "image":"/product.png","offers":{"price":"12.34","priceCurrency":"EUR"}}
            </script>
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
        Assert.Equal(
            12.34m,
            result.Price);
        Assert.Equal(
            "https://example.com/product.png",
            result.ImageUrl?.AbsoluteUri);
        Assert.False(result.CurrencyUnsupported);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("<title> Gift &amp; book </title>", "Gift & book")]
    [InlineData("<meta property='og:title' content='Gift'><title>Fallback</title>", "Gift")]
    [InlineData("<meta name='OG:TITLE' content='Gift'>", "Gift")]
    [InlineData("<meta property='og:title' content=' '><title>Fallback</title>", "Fallback")]
    [InlineData("<meta property='og:title'><title>Fallback</title>", "Fallback")]
    [InlineData("<meta property='og:title' content='One'><meta property='og:title' content='Two'>", null)]
    [InlineData("<script type='application/ld+json'>{invalid</script><title>Fallback</title>", "Fallback")]
    [InlineData("<script type='application/ld+json'>null</script>", null)]
    [InlineData("<script type='application/ld+json'>{}</script>", null)]
    [InlineData("<script type='application/ld+json'>{\"@type\":null}</script>", null)]
    [InlineData("<script type='application/ld+json'>{\"@type\":\"Thing\"}</script>", null)]
    [InlineData("<script type='application/ld+json'>{\"@graph\":[{\"@type\":[\"Thing\",\"Product\"],\"name\":\"Gift\"}]}</script>", "Gift")]
    [InlineData("<script type='application/ld+json'>[{\"@type\":\"https://schema.org/Product\",\"name\":\"Gift\"}]</script>", "Gift")]
    [InlineData("<script type='application/ld+json'>{\"@type\":\"http://schema.org/Product\",\"name\":\"Gift\"}</script>", "Gift")]
    [InlineData("<script type='application/ld+json'>[{\"@type\":\"Product\"},{\"@type\":\"Product\"}]</script><title>Ambiguous</title>", null)]
    public void Extract_WhenMetadataIsIncomplete_ReturnsOnlyUsableName(
        string html,
        string? name)
    {
        // Arrange
        var document = CreateDocument(html);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            name,
            result.Name);
        Assert.Null(result.Price);
        Assert.Null(result.ImageUrl);
    }

    [Theory]
    [InlineData("\"12.34\"", "\"EUR\"", "12.34", false)]
    [InlineData("12.34", "\"eur\"", "12.34", false)]
    [InlineData("\"12.34\"", "\"USD\"", null, true)]
    [InlineData("\"12.34\"", "null", null, true)]
    [InlineData("\"12.345\"", "\"EUR\"", null, false)]
    [InlineData("\"100000000\"", "\"EUR\"", null, false)]
    [InlineData("\"0\"", "\"EUR\"", null, false)]
    [InlineData("\"-1\"", "\"EUR\"", null, false)]
    [InlineData("\"NaN\"", "\"EUR\"", null, false)]
    [InlineData("null", "\"EUR\"", null, false)]
    [InlineData("{}", "\"EUR\"", null, false)]
    public void Extract_WhenPriceIsChecked_DoesNotGuessOrConvert(
        string amount,
        string currency,
        string? expectedPrice,
        bool unsupported)
    {
        // Arrange
        var document = CreateDocument($$$"""
            <script type="application/ld+json">
            {"@type":"Product","offers":{"price":{{{amount}}},"priceCurrency":{{{currency}}}}}
            </script>
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            expectedPrice,
            result.Price?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(
            unsupported,
            result.CurrencyUnsupported);
    }

    [Theory]
    [InlineData("[]", null)]
    [InlineData("[\"/first.png\",\"/second.png\"]", "https://example.com/first.png")]
    [InlineData("{\"url\":\"/image.png\"}", "https://example.com/image.png")]
    [InlineData("null", null)]
    [InlineData("\"http://[invalid\"", null)]
    public void Extract_WhenImageHasDifferentShapes_UsesAtMostOneCandidate(
        string image,
        string? expected)
    {
        // Arrange
        var document = CreateDocument($$"""
            <script type="application/ld+json">{"@type":"Product","image":{{image}}}</script>
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            expected,
            result.ImageUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("\"utf-8\"")]
    [InlineData("unknown-encoding")]
    [InlineData("")]
    public void Extract_WhenCharsetIsSpecified_UsesSafeEncodingFallback(string charset)
    {
        // Arrange
        var document = CreateDocument(
            "<title>Gift</title>",
            charset);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
    }

    [Fact]
    public void Extract_WhenPricesDifferAcrossOffers_LeavesPriceEmpty()
    {
        // Arrange
        var document = CreateDocument("""
            <script type="application/ld+json">
            {"@type":"Product","offers":[{"price":1,"priceCurrency":"EUR"},{"price":2,"priceCurrency":"EUR"}]}
            </script>
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Null(result.Price);
    }

    [Fact]
    public void Extract_WhenOpenGraphIsComplete_UsesMetadataWithoutFetchingResources()
    {
        // Arrange
        var document = CreateDocument("""
            <script>throw new Error("must not execute");</script>
            <meta property="og:title" content="Gift">
            <meta property="og:image" content="//images.example.com/image.png">
            <meta property="product:price:amount" content="42.50">
            <meta property="product:price:currency" content="EUR">
        """);

        // Act
        var result = _extractor.Extract(document);

        // Assert
        Assert.Equal(
            42.50m,
            result.Price);
        Assert.Equal(
            "https://images.example.com/image.png",
            result.ImageUrl?.AbsoluteUri);
    }

    private static ImportDocument CreateDocument(
        string html,
        string? charset = null)
    {

        return new ImportDocument
        {
            Url = new Uri("https://example.com/product"),
            Content = Encoding.UTF8.GetBytes(html),
            Charset = charset,
            MediaType = "text/html"
        };
    }
}
