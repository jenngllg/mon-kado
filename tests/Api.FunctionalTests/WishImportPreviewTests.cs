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
    [Fact]
    public async Task CreateAsync_WhenPostgreSqlIsUnavailable_DoesNotFetchMerchant()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        factory.WishlistService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            null);
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(Guid.CreateVersion7()),
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
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(Guid.CreateVersion7()),
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
        factory.ImportClient.Exception = new WishImportUrlRejectedException();
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(Guid.CreateVersion7()),
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
        factory.WishlistService.Access = WishlistAccess.NotOwned;
        using var client = authenticated ? CreateClient(
            factory,
            Guid.CreateVersion7()) : factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(Guid.CreateVersion7()),
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
        factory.ImportClient.Exception = new HttpRequestException("remote failure");
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            GetPath(Guid.CreateVersion7()),
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
        using var first = CreateClient(
            factory,
            Guid.CreateVersion7());
        using var second = CreateClient(
            factory,
            Guid.CreateVersion7());
        var path = GetPath(Guid.CreateVersion7());

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
