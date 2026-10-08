using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using Moq;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class ProductCatalogClientTests
{
    private const string ProductUrl = "https://www.sephora.fr/p/test-product-123456.html";
    private const string Data = """
        {"id":"P1234","variation_id":"123456","url":"https://www.sephora.fr/p/test-product-123456.html","name":"Product","price":{"salesPrice":29.9,"currency":"EUR"},"image_url":"http://media.sephora.eu/product.jpg","defaultVariantId":123456}
        """;
    private readonly Mock<IUrlImportClient> _clientMock;
    private readonly ProductCatalogClient _catalogClient;

    public ProductCatalogClientTests()
    {
        _clientMock = new(MockBehavior.Strict);
        _catalogClient = CreateClient(true);
    }

    [Theory]
    [InlineData("https://www.sephora.fr/p/test-product-123456.html", true)]
    [InlineData("https://sephora.fr/p/123456.html", true)]
    [InlineData("https://www.sephora.fr/p/test-product-P1000212958.html?tracking=x", true)]
    [InlineData("https://www.sephora.fr/p/test-product-123456.html?tracking=x", true)]
    [InlineData("http://www.sephora.fr/p/test-123456.html", false)]
    [InlineData("https://www.sephora.fr:444/p/test-123456.html", false)]
    [InlineData("https://user:secret@www.sephora.fr/p/test-123456.html", false)]
    [InlineData("https://www.sephora.fr.evil.example/p/test-123456.html", false)]
    [InlineData("https://www.sephora.fr/search", false)]
    [InlineData("https://www.sephora.fr/p/not-a-reference.html", false)]
    [InlineData("https://www.sephora.fr/p/1234567890123.html", false)]
    [InlineData("https://www.fnac.com/product-123456.html", false)]
    public void CanHandle_WhenAuthorityAndReferenceAreChecked_AcceptsOnlySupportedProducts(
        string url,
        bool expected)
    {
        // Arrange
        var product = new Uri(url);

        // Act
        var result = _catalogClient.CanHandle(product);

        // Assert
        Assert.Equal(
            expected,
            result);
        _clientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenDisabled_DoesNotCallPublicIndex()
    {
        // Arrange
        var client = CreateClient(false);

        // Act
        var result = await client.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Name);
        _clientMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_WhenExactVariantExists_ReturnsNormalizedMetadataWithoutFuzzySelection(bool duplicate)
    {
        // Arrange
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + Data + (duplicate ? ",\"variations\":[{\"data\":" + Data + "}]" : string.Empty) + "}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl + "?tracking=private"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "Product",
            result.Name);
        Assert.Equal(
            29.9m,
            result.Price);
        Assert.Equal(
            "https://media.sephora.eu/product.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit",
            result.ImageUrl?.AbsoluteUri);
        Assert.False(result.CurrencyUnsupported);
        VerifyRequest();
    }

    [Fact]
    public async Task GetAsync_WhenRootVariantDiffers_SelectsOnlyMatchingNestedVariant()
    {
        // Arrange
        var other = Data.Replace(
            "123456",
            "654321",
            StringComparison.Ordinal);
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + other + ",\"variations\":[{\"data\":" + Data + "}]}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            29.9m,
            result.Price);
        VerifyRequest();
    }

    [Fact]
    public async Task GetAsync_WhenMasterProductDeclaresDefault_UsesExplicitDefaultNotFirstOffer()
    {
        // Arrange
        var other = Data
            .Replace(
                "123456",
                "654321",
                StringComparison.Ordinal)
            .Replace(
                "29.9",
                "1.0",
                StringComparison.Ordinal)
            .Replace(
                "\"defaultVariantId\":654321",
                "\"defaultVariantId\":123456",
                StringComparison.Ordinal);
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + other + ",\"variations\":[{\"data\":" + Data + "}]}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri("https://www.sephora.fr/p/product-P1234.html"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            29.9m,
            result.Price);
        VerifyRequest("P1234");
    }

    [Theory]
    [InlineData("123456", "\"name\":\"Different\"", "\"name\":\"Product\"")]
    [InlineData("123456", "\"salesPrice\":10", "\"salesPrice\":29.9")]
    [InlineData("123456", "other.jpg", "product.jpg")]
    public async Task GetAsync_WhenMatchingVariantsConflict_DoesNotGuess(
        string id,
        string replacement,
        string original)
    {
        // Arrange
        var conflict = Data.Replace(
            original,
            replacement,
            StringComparison.Ordinal);
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + Data + ",\"variations\":[{\"data\":" + conflict + "}]}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Name);
        Assert.Null(result.Price);
        VerifyRequest(id);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"response\":[]}")]
    [InlineData("{\"response\":{}}")]
    [InlineData("{\"response\":{\"results\":{}}}")]
    [InlineData("{\"response\":{\"results\":[]}}")]
    [InlineData("{\"response\":{\"results\":[null,42,{}]}}")]
    [InlineData("{\"response\":{\"results\":[{\"data\":null}]}}")]
    public async Task GetAsync_WhenResponseHasNoExactProduct_ReturnsEmptySuggestions(string json)
    {
        // Arrange
        SetupResponse(json);

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Name);
        Assert.Null(result.Price);
        Assert.Null(result.ImageUrl);
        VerifyRequest();
    }

    [Theory]
    [InlineData("\"variation_id\":\"123456\"", "\"variation_id\":\"999999\"")]
    [InlineData("https://www.sephora.fr/p/test-product-123456.html", "https://evil.example/p/test-product-123456.html")]
    [InlineData("https://www.sephora.fr/p/test-product-123456.html", "https://www.sephora.fr/p/test-product-654321.html")]
    [InlineData("https://www.sephora.fr/p/test-product-123456.html", "not-a-url")]
    [InlineData("\"url\":\"https://www.sephora.fr/p/test-product-123456.html\"", "\"url\":123")]
    public async Task GetAsync_WhenIdentityDoesNotMatch_RejectsSearchSuggestion(
        string original,
        string replacement)
    {
        // Arrange
        var data = Data.Replace(
            original,
            replacement,
            StringComparison.Ordinal);
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + data + "}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Name);
        VerifyRequest();
    }

    [Theory]
    [InlineData("http://media.sephora.eu/product.jpg", "http://127.0.0.1/image.jpg")]
    [InlineData("http://media.sephora.eu/product.jpg", "https://media.sephora.eu.evil.example/image.jpg")]
    [InlineData("http://media.sephora.eu/product.jpg", "https://user:pass@media.sephora.eu/image.jpg")]
    [InlineData("http://media.sephora.eu/product.jpg", "https://media.sephora.eu:444/image.jpg")]
    [InlineData("http://media.sephora.eu/product.jpg", "ftp://media.sephora.eu/image.jpg")]
    [InlineData("http://media.sephora.eu/product.jpg", "not-an-image-url")]
    public async Task GetAsync_WhenImageIsNotPublicFirstPartyHttps_OmitsImage(
        string original,
        string replacement)
    {
        // Arrange
        var data = Data.Replace(
            original,
            replacement,
            StringComparison.Ordinal);
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + data + "}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "Product",
            result.Name);
        Assert.Null(result.ImageUrl);
        VerifyRequest();
    }

    [Theory]
    [InlineData("USD", true)]
    [InlineData("EUR", false)]
    public async Task GetAsync_WhenCurrencyIsUnsupported_PreservesNameWithoutInventingEuroPrice(
        string currency,
        bool unsupported)
    {
        // Arrange
        var data = Data.Replace(
            "EUR",
            currency,
            StringComparison.Ordinal);
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + data + "}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            unsupported,
            result.CurrencyUnsupported);
        Assert.Equal(
            unsupported ? null : 29.9m,
            result.Price);
        VerifyRequest();
    }

    [Theory]
    [InlineData("not-json", "application/json")]
    [InlineData("{}", "text/html")]
    public async Task GetAsync_WhenResponseIsInvalid_ReportsSafeRemoteFailure(
        string json,
        string mediaType)
    {
        // Arrange
        SetupResponse(
            json,
            mediaType);

        // Act
        var action = () => _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(action);
        VerifyRequest();
    }

    [Theory]
    [InlineData("\"id\":\"P1234\"", "\"id\":\"P9999\"")]
    [InlineData(",\"defaultVariantId\":123456", "")]
    [InlineData("\"defaultVariantId\":123456", "\"defaultVariantId\":\"123456\"")]
    [InlineData("\"defaultVariantId\":123456", "\"defaultVariantId\":123456789012345678901234567890")]
    public async Task GetAsync_WhenMasterIdentityOrDefaultIsMissing_DoesNotChooseFirstVariant(
        string original,
        string replacement)
    {
        // Arrange
        var data = Data.Replace(
            original,
            replacement,
            StringComparison.Ordinal);
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + data + "}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri("https://www.sephora.fr/p/product-P1234.html"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Name);
        VerifyRequest("P1234");
    }

    [Theory]
    [InlineData(",\"name\":\"Product\"", "")]
    [InlineData(",\"price\":{\"salesPrice\":29.9,\"currency\":\"EUR\"}", "")]
    [InlineData("\"price\":{\"salesPrice\":29.9,\"currency\":\"EUR\"}", "\"price\":null")]
    [InlineData("\"price\":{\"salesPrice\":29.9,\"currency\":\"EUR\"}", "\"price\":{\"currency\":\"EUR\"}")]
    public async Task GetAsync_WhenOptionalMetadataIsAbsent_DoesNotInventIt(
        string original,
        string replacement)
    {
        // Arrange
        var data = Data.Replace(
            original,
            replacement,
            StringComparison.Ordinal);
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + data + "}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.ImageUrl);
        VerifyRequest();
    }

    [Fact]
    public async Task GetAsync_WhenVariationsContainMalformedEntries_IgnoresThem()
    {
        // Arrange
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + Data + ",\"variations\":[null,{}, {\"data\":null}]}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "Product",
            result.Name);
        VerifyRequest();
    }

    [Fact]
    public async Task GetAsync_WhenTwoResultsClaimExactVariant_RejectsAmbiguity()
    {
        // Arrange
        SetupResponse("{\"response\":{\"results\":[{\"data\":" + Data + "},{\"data\":" + Data + "}]}}");

        // Act
        var result = await _catalogClient.GetAsync(
            new Uri(ProductUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Name);
        VerifyRequest();
    }

    [Fact]
    public void CanHandle_WhenUrlIsRelative_RejectsWithoutResolvingAuthority()
    {
        // Arrange
        var url = new Uri(
            "/p/product-123456.html",
            UriKind.Relative);

        // Act
        var result = _catalogClient.CanHandle(url);

        // Assert
        Assert.False(result);
        _clientMock.VerifyNoOtherCalls();
    }

    private ProductCatalogClient CreateClient(bool enabled)
    {

        return new ProductCatalogClient(
            _clientMock.Object,
            new MerchantMetadataExtractor(),
            Microsoft.Extensions.Options.Options.Create(new ProductCatalogOptions
            {
                Enabled = enabled,
                IndexKey = "key_public_test_index"
            }));
    }

    private void SetupResponse(
        string json,
        string mediaType = "application/json")
    {
        _clientMock
            .Setup(client => client.DownloadAsync(
                It.Is<Uri>(url => url.Host == "ac.cnstrc.com"),
                262144,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(new ImportDocument
            {
                Url = new Uri("https://ac.cnstrc.com"),
                Content = Encoding.UTF8.GetBytes(json),
                MediaType = mediaType,
                Charset = "utf-8"
            });
    }

    private void VerifyRequest(string id = "123456")
    {
        _clientMock.Verify(
            client => client.DownloadAsync(
                new Uri("https://ac.cnstrc.com/search/" + id + "?key=key_public_test_index&num_results_per_page=3"),
                262144,
                TestContext.Current.CancellationToken),
            Times.Once);
        _clientMock.VerifyNoOtherCalls();
    }
}
