using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishImportPreviewTests
{
    [Theory]
    [InlineData("123456")]
    [InlineData("P1234")]
    public async Task CreateAsync_WhenPublicCatalogIsEnabled_ImportsExactVariantWithoutStorefrontDownload(string reference)
    {
        // Arrange
        await using var factory = new WishImportApiFactory { ProductCatalogEnabled = true };
        var url = "https://www.sephora.fr/p/generic-product-" + reference + ".html?tracking=original";
        factory.ImportClient.CatalogJson = """
            {"response":{"results":[{"data":{"id":"P1234","variation_id":"654321","defaultVariantId":123456,
            "url":"https://www.sephora.fr/p/generic-product-654321.html","name":"Other format",
            "price":{"salesPrice":10,"currency":"EUR"}},
            "variations":[{"data":{"variation_id":"123456","url":"https://www.sephora.fr/p/generic-product-123456.html",
            "name":"Selected format","price":{"salesPrice":29.90,"currency":"EUR"},
            "image_url":"http://media.sephora.eu/selected.png"}}]}]}}
            """;
        using var bitmap = new SKBitmap(
            2,
            2);
        bitmap.Erase(SKColors.Green);
        using var png = bitmap.Encode(
            SKEncodedImageFormat.Png,
            100);
        factory.ImportClient.ImageContent = png.ToArray();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url
            },
            TestContext.Current.CancellationToken);
        var preview = await response.Content.ReadFromJsonAsync<WishImportPreview>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal(
            "Selected format",
            preview.Name);
        Assert.Equal(
            29.9m,
            preview.Price);
        Assert.Equal(
            url,
            preview.Url);
        Assert.NotNull(preview.Image);
        Assert.Empty(preview.Warnings);
        Assert.Equal(
            [
                new Uri("https://ac.cnstrc.com/search/" + reference + "?key=key_public_test_index&num_results_per_page=3"),
                new Uri("https://media.sephora.eu/selected.png?scaleWidth=750&scaleHeight=750&scaleMode=fit")
            ],
            factory.ImportClient.Requests);
        Assert.Empty(factory.WishService.Creations);
        Assert.False(Directory.Exists(factory.StoragePath));
    }

    [Fact]
    public async Task CreateAsync_WhenPublicCatalogRefusesRequest_ReturnsManualCompletionWithoutRetry()
    {
        // Arrange
        await using var factory = new WishImportApiFactory { ProductCatalogEnabled = true };
        factory.ImportClient.Exception = new HttpRequestException(
            "Denied",
            null,
            HttpStatusCode.Forbidden);
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url = "https://www.sephora.fr/p/product-123456.html"
            },
            TestContext.Current.CancellationToken);
        var preview = await response.Content.ReadFromJsonAsync<WishImportPreview>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.NotNull(preview);
        Assert.Null(preview.Name);
        Assert.Contains(
            "WISH_IMPORT_PAGE_UNAVAILABLE",
            preview.Warnings);
        Assert.Single(factory.ImportClient.Requests);
        Assert.Empty(factory.WishService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenMicrodataContainsSeveralFormats_ImportsOnlyLinkedVariant()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        const string url = "https://www.sephora.fr/p/generic-fragrance-123456.html";
        factory.ImportClient.Html = $$"""
            <main itemscope itemtype='https://schema.org/Product'>
              <span itemprop='brand' itemscope itemtype='https://schema.org/Brand'><span itemprop='name'>Brand</span></span>
              <meta itemprop='name' content='Fragrance 50 ml'><meta itemprop='image' content='https://cdn.example.com/selected.png'>
              <div itemprop='offers' itemscope itemtype='https://schema.org/Offer'>
                <meta itemprop='price' content='29.90'><meta itemprop='priceCurrency' content='EUR'>
                <meta itemprop='url' content='/p/generic-fragrance-travel-654321.html'>
              </div>
              <div itemprop='offers' itemscope itemtype='https://schema.org/Offer'>
                <meta itemprop='price' content='97.30'><meta itemprop='priceCurrency' content='EUR'>
                <meta itemprop='url' content='{{url}}'>
              </div>
            </main>
            """;
        using var bitmap = new SKBitmap(
            2,
            2);
        bitmap.Erase(SKColors.Green);
        using var png = bitmap.Encode(
            SKEncodedImageFormat.Png,
            100);
        factory.ImportClient.ImageContent = png.ToArray();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url
            },
            TestContext.Current.CancellationToken);
        var preview = await response.Content.ReadFromJsonAsync<WishImportPreview>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal(
            "Fragrance 50 ml",
            preview.Name);
        Assert.Equal(
            97.30m,
            preview.Price);
        Assert.NotNull(preview.Image);
        Assert.Equal(
            "image/webp",
            preview.Image.ContentType);
        Assert.Empty(preview.Warnings);
        Assert.Equal(
            [
                new Uri(url),
                new Uri("https://cdn.example.com/selected.png")
            ],
            factory.ImportClient.Requests);
        Assert.Empty(factory.WishService.Creations);
        Assert.False(Directory.Exists(factory.StoragePath));
    }

    [Theory]
    [InlineData("https://www.cultura.com/p-a-book.html")]
    [InlineData("https://www.cultura.com/p-another-book.html?origin=affiliate")]
    public async Task CreateAsync_WhenProductHasReviewsAndMalformedDescription_ImportsMainOfferAndVisibleImage(string url)
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var mainUrl = new Uri(url).GetLeftPart(UriPartial.Path);
        factory.ImportClient.Html = $$$"""
            <script type="application/ld+json">
            {"@type":"Product","name":"A book","description":"First line
            Second line","image":"media/cover","offers":{"url":"{{{mainUrl}}}","price":"14.95","priceCurrency":"EUR"}}
            </script>
            <script type="application/ld+json">{"@type":"Product","name":"A book","review":{"@type":"Review"}}</script>
            <img alt="media/cover" src="https://cdn.example.com/cover.png">
            """;
        using var bitmap = new SKBitmap(
            2,
            2);
        bitmap.Erase(SKColors.Green);
        using var png = bitmap.Encode(
            SKEncodedImageFormat.Png,
            100);
        factory.ImportClient.ImageContent = png.ToArray();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url
            },
            TestContext.Current.CancellationToken);
        var preview = await response.Content.ReadFromJsonAsync<WishImportPreview>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal(
            "A book",
            preview.Name);
        Assert.Equal(
            14.95m,
            preview.Price);
        Assert.Equal(
            url,
            preview.Url);
        Assert.NotNull(preview.Image);
        Assert.Equal(
            "image/webp",
            preview.Image.ContentType);
        Assert.Empty(preview.Warnings);
        Assert.Equal(
            [
                new Uri(url),
                new Uri("https://cdn.example.com/cover.png")
            ],
            factory.ImportClient.Requests);
        Assert.Empty(factory.WishService.Creations);
        Assert.False(Directory.Exists(factory.StoragePath));
    }

    [Fact]
    public async Task CreateAsync_WhenStoreReturnsBrowserChallenge_DoesNotImportStoreLogo()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        factory.ImportClient.Html = """
            <title>Cdiscount</title><meta property="og:title" content="Cdiscount.com"><meta property="og:image" content="https://images.example.com/logo.png">
            <script>var url = '/cdn-cgi/challenge-platform/scripts/jsd/main.js';</script>
            """;
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());
        const string url = "https://www.cdiscount.com/product.html";

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url
            },
            TestContext.Current.CancellationToken);
        var preview = await response.Content.ReadFromJsonAsync<WishImportPreview>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.NotNull(preview);
        Assert.Null(preview.Name);
        Assert.Null(preview.Price);
        Assert.Null(preview.Image);
        Assert.Equal(
            url,
            preview.Url);
        Assert.Equal(
            3,
            preview.Warnings.Count());
        Assert.Equal(
            new Uri(url),
            Assert.Single(factory.ImportClient.Requests));
        Assert.Empty(factory.WishService.Creations);
    }

    [Theory]
    [InlineData("https://www.fnac.com/a123456/A-book")]
    [InlineData("https://www.decathlon.fr/p/example/_/R-p-123456")]
    public async Task CreateAsync_WhenStoreRefusesAutomatedAccess_KeepsLinkAndAllowsManualCompletion(string url)
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        factory.ImportClient.Exception = new HttpRequestException(
            "Access denied",
            null,
            HttpStatusCode.Forbidden);
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url
            },
            TestContext.Current.CancellationToken);
        var preview = await response.Content.ReadFromJsonAsync<WishImportPreview>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal(
            url,
            preview.Url);
        Assert.Null(preview.Name);
        Assert.Null(preview.Price);
        Assert.Null(preview.Image);
        Assert.Contains(
            "WISH_IMPORT_PAGE_UNAVAILABLE",
            preview.Warnings);
        Assert.Equal(
            4,
            preview.Warnings.Count());
        Assert.Equal(
            new Uri(url),
            Assert.Single(factory.ImportClient.Requests));
        Assert.Empty(factory.WishService.Creations);
    }

    [Theory]
    [InlineData("https://www.amazon.fr/dp/B0EXAMPLE01?ref=sharing")]
    [InlineData("https://www.amazon.de/Example/dp/B0EXAMPLE02")]
    [InlineData("https://www.amazon.com/gp/product/B0EXAMPLE03")]
    public async Task CreateAsync_WhenAmazonProductIsComplete_ReturnsNormalizedSuggestionsWithoutCreatingWish(string url)
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        factory.ImportClient.Html = """
            <title>Amazon.fr</title>
            <span id="productTitle">A book for everyone</span>
            <img id="landingImage" data-old-hires="https://images.example.com/cover.png" src="/small.png">
            <div id="corePriceDisplay_desktop_feature_div"><span class="priceToPay"><span class="a-offscreen">9,90 €</span></span></div>
            """;
        using var bitmap = new SKBitmap(
            2,
            2);
        bitmap.Erase(SKColors.Green);
        using var png = bitmap.Encode(
            SKEncodedImageFormat.Png,
            100);
        factory.ImportClient.ImageContent = png.ToArray();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url
            },
            TestContext.Current.CancellationToken);
        var preview = await response.Content.ReadFromJsonAsync<WishImportPreview>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotNull(preview);
        Assert.Equal(
            "A book for everyone",
            preview.Name);
        Assert.Equal(
            url,
            preview.Url);
        Assert.Equal(
            9.90m,
            preview.Price);
        Assert.Equal(
            1,
            preview.Quantity);
        Assert.NotNull(preview.Image);
        Assert.Equal(
            "image/webp",
            preview.Image.ContentType);
        Assert.NotNull(preview.Image.ContentBase64);
        Assert.NotEmpty(Convert.FromBase64String(preview.Image.ContentBase64));
        Assert.Empty(preview.Warnings);
        Assert.Equal(
            [
                new Uri(url),
                new Uri("https://images.example.com/cover.png")
            ],
            factory.ImportClient.Requests);
        Assert.Empty(factory.WishService.Creations);
        Assert.False(Directory.Exists(factory.StoragePath));
    }

    [Theory]
    [InlineData("<title>Amazon.fr</title>")]
    [InlineData("<title>Robot Check</title><form action='/errors/validateCaptcha'><input id='captchacharacters'></form>")]
    public async Task CreateAsync_WhenAmazonDoesNotReturnProductMarkup_OffersManualCompletionWithoutFetchingImage(string html)
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        factory.ImportClient.Html = html;
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());
        const string url = "https://www.amazon.fr/dp/B0EXAMPLE01";

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url
            },
            TestContext.Current.CancellationToken);
        var preview = await response.Content.ReadFromJsonAsync<WishImportPreview>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal(
            url,
            preview.Url);
        Assert.Null(preview.Name);
        Assert.Null(preview.Price);
        Assert.Null(preview.Image);
        Assert.Equal(
            3,
            preview.Warnings.Count());
        Assert.Equal(
            new Uri(url),
            Assert.Single(factory.ImportClient.Requests));
        Assert.Empty(factory.WishService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenOnlyCookiesArePresent_ReturnsUnauthorizedWithoutFetchingMerchant()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Cookie",
            "MonKado.Refresh=opaque-refresh; __Host-MonKado.Refresh=opaque-refresh; .AspNetCore.Identity.Application=opaque-ticket");

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url = "https://merchant.example"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.ImportClient.Requests);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data")]
    public async Task CreateAsync_WhenContentTypeIsNotJson_RejectsBeforeFetchingMerchant(string contentType)
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());
        using var content = new StringContent("url=https://merchant.example");
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        // Act
        using var response = await client.PostAsync(
            GetPath(wishlistId),
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        Assert.Empty(factory.ImportClient.Requests);
    }

    [Fact]
    public async Task CreateAsync_WhenPostgreSqlIsUnavailable_DoesNotFetchMerchant()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        factory.WishlistService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            null);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url = "https://merchant.example"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Empty(factory.ImportClient.Requests);
    }

    [Fact]
    public async Task CreateAsync_WhenPageIsPartial_ReturnsExactContractWithoutPersistingOrLoggingContent()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());
        const string merchantUrl = "https://merchant.example/product?sensitive=value";

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url = merchantUrl
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var preview = json.RootElement;
        Assert.Equal(
            [
                "name",
                "url",
                "price",
                "quantity",
                "image",
                "warnings"
            ],
            preview
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            "Imported gift",
            preview
                .GetProperty("name")
                .GetString());
        Assert.Equal(
            merchantUrl,
            preview
                .GetProperty("url")
                .GetString());
        Assert.Equal(
            JsonValueKind.Null,
            preview
                .GetProperty("price")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            preview
                .GetProperty("image")
                .ValueKind);
        Assert.Equal(
            1,
            preview
                .GetProperty("quantity")
                .GetInt32());
        Assert.Equal(
            2,
            preview
                .GetProperty("warnings")
                .GetArrayLength());
        Assert.Single(factory.ImportClient.Requests);
        Assert.Empty(factory.WishService.Creations);
        Assert.False(Directory.Exists(factory.StoragePath));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "sensitive",
                StringComparison.Ordinal) || message.Contains(
                "Imported gift",
                StringComparison.Ordinal) || message.Contains(
                "merchant.example",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a URL")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:secret@example.com")]
    [InlineData("https://example.com:8080")]
    public async Task CreateAsync_WhenUrlIsInvalid_ReturnsValidationErrorWithoutNetworkAccess(string? url)
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            "url",
            json.RootElement.GetProperty("validationErrors")[0]
                .GetProperty("propertyName")
                .GetString());
        Assert.Empty(factory.ImportClient.Requests);
    }

    [Fact]
    public async Task CreateAsync_WhenDestinationIsRejected_ReturnsStructuredBadRequest()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        factory.ImportClient.Exception = new WishImportUrlRejectedException();
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url = "https://merchant.example"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            "WISH_IMPORT_URL_REJECTED",
            json.RootElement
                .GetProperty("errorCode")
                .GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAsync_WhenCallerCannotAccessWishlist_DoesNotFetchMerchant(bool authenticated)
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        factory.WishlistService.Access = WishlistAccess.NotOwned;
        using var client = authenticated ? CreateClient(
            factory,
            Guid.CreateVersion7()) : factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url = "https://merchant.example"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            authenticated ? HttpStatusCode.NotFound : HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.ImportClient.Requests);
    }

    [Fact]
    public async Task CreateAsync_WhenMerchantFails_ReturnsManualCompletionInsteadOfServerError()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        factory.ImportClient.Exception = new HttpRequestException("remote failure");
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url = "https://merchant.example"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null(json.RootElement
                .GetProperty("name")
                .GetString());
        Assert.Contains(
            json.RootElement
                .GetProperty("warnings")
                .EnumerateArray(),
            warning => warning.GetString() == "WISH_IMPORT_PAGE_UNAVAILABLE");
    }

    [Fact]
    public async Task CreateAsync_WhenMemberExceedsQuota_SeparatesMembersAtSameAddress()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var first = CreateClient(
            factory,
            Guid.CreateVersion7());
        using var second = CreateClient(
            factory,
            Guid.CreateVersion7());
        var path = GetPath(wishlistId);

        // Act
        for (var index = 0; index < 10; index++)
        {
            using var accepted = await first.PostAsJsonAsync(
                path,
                new
                {
                    url = "https://merchant.example"
                },
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.OK,
                accepted.StatusCode);
        }

        using var rejected = await first.PostAsJsonAsync(
            path,
            new
            {
                url = "https://merchant.example"
            },
            TestContext.Current.CancellationToken);
        using var other = await second.PostAsJsonAsync(
            path,
            new
            {
                url = "https://merchant.example"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal(
            HttpStatusCode.OK,
            other.StatusCode);
        Assert.Equal(
            11,
            factory.ImportClient.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_WhenConfirmed_KeepsCreatedGiftEvenIfImageUploadFails(bool validUpload)
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        factory.ImportClient.Html = """
            <meta property="og:title" content="Imported gift">
            <meta property="og:image" content="/image.png">
        """;
        using var bitmap = new SKBitmap(
            2,
            2);
        bitmap.Erase(SKColors.Red);
        using var png = bitmap.Encode(
            SKEncodedImageFormat.Png,
            100);
        factory.ImportClient.ImageContent = png.ToArray();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.SeedActiveWishlist(wishlistId);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var previewResponse = await client.PostAsJsonAsync(
            GetPath(wishlistId),
            new
            {
                url = "https://merchant.example"
            },
            TestContext.Current.CancellationToken);
        var preview = await previewResponse.Content.ReadFromJsonAsync<WishImportPreview>(TestContext.Current.CancellationToken);
        Assert.NotNull(preview?.Image);
        using var created = await client.PostAsJsonAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes",
            new
            {
                name = preview.Name,
                url = preview.Url,
                quantity = preview.Quantity
            },
            TestContext.Current.CancellationToken);
        using var wish = JsonDocument.Parse(await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var wishId = wish.RootElement
            .GetProperty("id")
            .GetGuid();
        using var upload = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image");
        upload.Headers.IfMatch.Add(created.Headers.ETag!);
        using var form = new MultipartFormDataContent();
        var imageBytes = validUpload ? Convert.FromBase64String(preview.Image.ContentBase64!) : new byte[]
        {
            1,
            2,
            3
        };
        form.Add(
            new ByteArrayContent(imageBytes),
            "image",
            "image.webp");
        upload.Content = form;
        using var uploaded = await client.SendAsync(
            upload,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            previewResponse.StatusCode);
        Assert.Equal(
            "image/webp",
            preview.Image.ContentType);
        Assert.Equal(
            HttpStatusCode.Created,
            created.StatusCode);
        Assert.Equal(
            validUpload ? HttpStatusCode.OK : HttpStatusCode.UnsupportedMediaType,
            uploaded.StatusCode);
        Assert.Single(factory.WishService.Creations);
        Assert.True(factory.WishService.Wishes.ContainsKey((wishlistId, wishId)));
    }

    private static string GetPath(Guid wishlistId)
    {

        return $"/api/v1/wishlists/{wishlistId}/wish-import-previews";
    }

    private static HttpClient CreateClient(
        WishImportApiFactory factory,
        Guid ownerId)
    {
        var client = factory.CreateClient();
        var token = factory.Services
            .GetRequiredService<IAccessTokenService>()
            .Create(ownerId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.Value);

        return client;
    }
}
