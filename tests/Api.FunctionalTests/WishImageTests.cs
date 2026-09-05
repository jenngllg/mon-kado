using JennGllg.Fr.MonKado.Back.Api.Models;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

using SkiaSharp;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishImageTests
{
    private const string CurrentEntityTag = "\"0000002a\"";

    private static readonly byte[] _pngContent = CreatePng();

    [Theory]
    [InlineData(null, 428)]
    [InlineData("invalid", 400)]
    [InlineData("\"00000001\"", 412)]
    [InlineData("\"0000002b\"", 204)]
    public async Task DeleteImageAsync_WhenPreconditionIsEvaluated_ReturnsExpectedResponse(
        string? entityTag,
        int expectedStatus)
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var upload = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image");
        using var uploaded = await client.SendAsync(
            upload,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            uploaded.StatusCode);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image");

        if (entityTag is not null)
            request.Headers.TryAddWithoutValidation(
                HeaderNames.IfMatch,
                entityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            (int)response.StatusCode);

        if (expectedStatus == 204)
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal(
                "\"0000002c\"",
                response.Headers.ETag?.Tag);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            Assert.Null(factory.WishService.Wishes[(wishlistId, wishId)].ImageId);
        }
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 404)]
    public async Task DeleteImageAsync_WhenImageIsAbsent_ReturnsExpectedError(
        bool authenticated,
        int expectedStatus)
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = authenticated
            ? CreateAuthorizedClient(
                factory,
                Guid.CreateVersion7())
            : factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image");
        request.Headers.TryAddWithoutValidation(
            HeaderNames.IfMatch,
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            (int)response.StatusCode);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenPngIsValid_ReturnsSignedImageUrlAndDeliversNormalizedWebp()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.True(
            response.IsSuccessStatusCode,
            responseBody);
        using var document = JsonDocument.Parse(responseBody);
        var imageUrl = document.RootElement.GetProperty("imageUrl").GetString()
            ?? throw new InvalidOperationException("The signed gift-image URL is missing.");
        using var imageResponse = await client.GetAsync(
            imageUrl,
            TestContext.Current.CancellationToken);
        var normalizedContent = await imageResponse.Content.ReadAsByteArrayAsync(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002b\"",
            response.Headers.ETag?.Tag);
        Assert.Equal(
            [
                "id",
                "wishlistId",
                "name",
                "note",
                "url",
                "price",
                "quantity",
                "position",
                "createdAt",
                "updatedAt",
                "imageUrl"
            ],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            "/api/v1/wishlists/" + wishlistId + "/wishes/" + wishId + "/image",
            new Uri(imageUrl).AbsolutePath);
        var upsert = Assert.Single(factory.WishService.ImageUpserts);
        Assert.Equal(
            ownerId,
            upsert.OwnerId);
        Assert.Equal(
            wishlistId,
            upsert.WishlistId);
        Assert.Equal(
            wishId,
            upsert.WishId);
        Assert.Equal(
            32,
            upsert.ContentHash.Length);
        Assert.Equal(
            42u,
            upsert.ExpectedVersion);
        Assert.Equal(
            HttpStatusCode.OK,
            imageResponse.StatusCode);
        Assert.Equal(
            "image/webp",
            imageResponse.Content.Headers.ContentType?.MediaType);
        Assert.True(imageResponse.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "nosniff",
            Assert.Single(imageResponse.Headers.GetValues(HeaderNames.XContentTypeOptions)));
        Assert.True(normalizedContent.AsSpan(0, 4).SequenceEqual("RIFF"u8));
        Assert.True(normalizedContent.AsSpan(8, 4).SequenceEqual("WEBP"u8));
        Assert.Equal(
            [(ownerId, wishlistId, wishId, upsert.ImageId)],
            factory.WishImageAccessService.OwnedChecks);
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                imageUrl,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetSharedImageAsync_WhenShareIsCurrent_DeliversNormalizedWebp()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        using var upsertRequest = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image");
        using var upsertResponse = await ownerClient.SendAsync(
            upsertRequest,
            TestContext.Current.CancellationToken);
        var imageId = Assert.Single(factory.WishService.ImageUpserts).ImageId;
        factory.WishlistShareService.SharedWishlist = new SharedWishlistDetails(
            wishlistId,
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            [
                new SharedWishDetails(
                    wishId,
                    "Gift",
                    null,
                    null,
                    imageId: imageId)
            ]);
        using var client = factory.CreateClient();
        using var sharedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}");
        sharedRequest.Headers.Add(
            "X-MonKado-Share-Token",
            "public-secret");
        using var sharedResponse = await client.SendAsync(
            sharedRequest,
            TestContext.Current.CancellationToken);
        using var document = await sharedResponse.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The shared wishlist response is empty.");
        var wish = Assert.Single(document.RootElement.GetProperty("wishes").EnumerateArray());
        var imageUrl = wish.GetProperty("imageUrl").GetString()
            ?? throw new InvalidOperationException("The shared gift-image URL is missing.");

        // Act
        using var imageResponse = await client.GetAsync(
            imageUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            upsertResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            sharedResponse.StatusCode);
        Assert.Equal(
            $"/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/image",
            new Uri(imageUrl).AbsolutePath);
        Assert.Equal(
            HttpStatusCode.OK,
            imageResponse.StatusCode);
        Assert.Equal(
            [(shareLinkId, wishlistId, wishId, imageId)],
            factory.WishImageAccessService.SharedChecks);
    }

    [Fact]
    public async Task GetOwnedImageAsync_WhenGrantIsNoLongerCurrent_ReturnsNonDisclosingNotFound()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image");
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-image response is empty.");
        var imageUrl = document.RootElement.GetProperty("imageUrl").GetString()
            ?? throw new InvalidOperationException("The signed gift-image URL is missing.");
        factory.WishImageAccessService.IsOwnedCurrent = false;

        // Act
        using var imageResponse = await client.GetAsync(
            imageUrl,
            TestContext.Current.CancellationToken);
        using var error = await imageResponse.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-image error response is empty.");

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            imageResponse.StatusCode);
        Assert.Equal(
            "WISH_IMAGE_NOT_FOUND",
            error.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("wrong", false)]
    [InlineData("image", true)]
    public async Task UpsertImageAsync_WhenMultipartShapeIsInvalid_ReturnsBadRequest(
        string fieldName,
        bool addTextField)
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            fieldName == "missing"
                ? null
                : _pngContent,
            fieldName,
            addTextField: addTextField);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.WishService.ImageUpserts);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenActualFormatIsUnsupported_ReturnsUnsupportedMediaType()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            "<svg></svg>"u8.ToArray(),
            "image");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        using var error = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-image error response is empty.");

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        Assert.Equal(
            "WISH_IMAGE_UNSUPPORTED_FORMAT",
            error.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(factory.WishService.ImageUpserts);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenRecognizedContentIsCorrupt_ReturnsBadRequest()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            [
                0xFF,
                0xD8,
                0xFF,
                0x00
            ],
            "image");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        using var error = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-image error response is empty.");

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            "WISH_IMAGE_INVALID",
            error.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(factory.WishService.ImageUpserts);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenFileExceedsMaximumSize_ReturnsPayloadTooLarge()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            new byte[GiftImageConstraints.MaximumInputLength + 1],
            "image");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        using var error = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-image error response is empty.");

        // Assert
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Equal(
            "REQUEST_PAYLOAD_TOO_LARGE",
            error.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(factory.WishService.ImageUpserts);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenContentTypeIsNotMultipart_ReturnsUnsupportedMediaType()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image")
        {
            Content = new ByteArrayContent(_pngContent)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/octet-stream");
        request.Headers.TryAddWithoutValidation(
            HeaderNames.IfMatch,
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        Assert.Empty(factory.WishService.ImageUpserts);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("malformed", true)]
    public async Task UpsertImageAsync_WhenBearerTokenIsUnavailable_ReturnsUnauthorized(
        string? accessToken,
        bool hasRemoteAddress)
    {
        // Arrange
        await using var factory = new GiftImageApiFactory(hasRemoteAddress
            ? IPAddress.Loopback
            : null);
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        using var client = factory.CreateClient();

        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                JwtBearerDefaults.AuthenticationScheme,
                accessToken);
        }

        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.WishService.ImageUpserts);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenWishDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        Assert.Single(factory.WishService.ImageUpserts);
        Assert.Single(Directory.EnumerateFiles(
            factory.StoragePath,
            "*.pending",
            SearchOption.AllDirectories));
    }

    [Fact]
    public async Task GetOwnedImageAsync_WhenReferencedFileIsMissing_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image");
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-image response is empty.");
        var imageUrl = document.RootElement.GetProperty("imageUrl").GetString()
            ?? throw new InvalidOperationException("The signed gift-image URL is missing.");
        var imagePath = Assert.Single(Directory.EnumerateFiles(
            factory.StoragePath,
            "*.webp",
            SearchOption.AllDirectories));
        File.Delete(imagePath);

        // Act
        using var imageResponse = await client.GetAsync(
            imageUrl,
            TestContext.Current.CancellationToken);
        using var error = await imageResponse.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-image error response is empty.");

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            imageResponse.StatusCode);
        Assert.Equal(
            "TECHNICAL_DEPENDENCY_UNAVAILABLE",
            error.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetImageAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailable(
        bool isShared)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var ownerId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var grant = new WishImageGrant
        {
            Version = 1,
            Scope = isShared
                ? "shared"
                : "owned",
            OwnerId = isShared
                ? null
                : ownerId,
            ShareLinkId = isShared
                ? shareLinkId
                : null,
            WishlistId = wishlistId,
            WishId = wishId,
            ImageId = Guid.CreateVersion7(),
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };
        var dataProtectionProvider = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = dataProtectionProvider.CreateProtector("MonKado.WishImages.Url.v1");
        var token = protector.Protect(JsonSerializer.Serialize(grant));
        var path = isShared
            ? $"/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/image"
            : $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image";
        var requestUri = QueryHelpers.AddQueryString(
            path,
            "token",
            token);

        // Act
        using var response = await client.GetAsync(
            requestUri,
            TestContext.Current.CancellationToken);
        using var error = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-image error response is empty.");

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            "TECHNICAL_DEPENDENCY_UNAVAILABLE",
            error.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task UpsertImageAsync_WhenMultipartContainsMultipleFiles_ReturnsBadRequest()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image",
            addSecondFile: true);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.WishService.ImageUpserts);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenEntityTagIsMissing_ReturnsPreconditionRequired()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image",
            entityTag: null);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            (HttpStatusCode)428,
            response.StatusCode);
        Assert.Empty(factory.WishService.ImageUpserts);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenWishVersionIsStale_ReturnsPreconditionFailed()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        factory.WishService.Exception = new WishVersionConflictException();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateUpsertRequest(
            wishlistId,
            wishId,
            _pngContent,
            "image");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            response.StatusCode);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenMemberExceedsPerMinuteLimit_ReturnsTooManyRequests()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        var responses = new List<HttpResponseMessage>();

        // Act
        for (var index = 0; index < 11; index++)
        {
            using var request = CreateUpsertRequest(
                wishlistId,
                wishId,
                _pngContent,
                "image");
            responses.Add(await client.SendAsync(
                request,
                TestContext.Current.CancellationToken));
        }

        // Assert
        try
        {
            Assert.All(
                responses.Take(10),
                response => Assert.Equal(
                    HttpStatusCode.OK,
                    response.StatusCode));
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                responses[10].StatusCode);
            var retryAfter = responses[10].Headers.RetryAfter?.ToString();
            Assert.NotNull(retryAfter);
            Assert.NotEmpty(retryAfter);
            Assert.Equal(
                10,
                factory.WishService.ImageUpserts.Count);
        }
        finally
        {
            responses.ForEach(response => response.Dispose());
        }
    }

    [Fact]
    public async Task UpsertImageAsync_WhenMembersShareAnAddress_AppliesIndependentLimits()
    {
        // Arrange
        await using var factory = new GiftImageApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateWish(
            wishlistId,
            wishId);
        using var firstClient = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var secondClient = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        var responses = new List<HttpResponseMessage>();

        // Act
        for (var index = 0; index < 10; index++)
        {
            foreach (var client in new[]
            {
                firstClient,
                secondClient
            })
            {
                using var request = CreateUpsertRequest(
                    wishlistId,
                    wishId,
                    _pngContent,
                    "image");
                responses.Add(await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken));
            }
        }

        // Assert
        try
        {
            Assert.All(
                responses,
                response => Assert.Equal(
                    HttpStatusCode.OK,
                    response.StatusCode));
            Assert.Equal(
                20,
                factory.WishService.ImageUpserts.Count);
        }
        finally
        {
            responses.ForEach(response => response.Dispose());
        }
    }

    private static WishDetails CreateWish(
        Guid wishlistId,
        Guid wishId)
    {
        return new WishDetails(
            wishId,
            wishlistId,
            "Gift",
            null,
            null,
            null,
            1,
            new DateTime(
                2026,
                9,
                5,
                12,
                0,
                0,
                DateTimeKind.Utc),
            null,
            42);
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(
            2,
            2,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        bitmap.Erase(SKColors.Purple);
        using var image = SKImage.FromBitmap(bitmap);
        using var content = image.Encode(
            SKEncodedImageFormat.Png,
            100);

        return content.ToArray();
    }

    private static HttpClient CreateAuthorizedClient(
        GiftImageApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
    }

    private static HttpRequestMessage CreateUpsertRequest(
        Guid wishlistId,
        Guid wishId,
        byte[]? content,
        string fieldName,
        string? entityTag = CurrentEntityTag,
        bool addTextField = false,
        bool addSecondFile = false)
    {
        var multipart = new MultipartFormDataContent();

        if (content is not null)
        {
            var image = new ByteArrayContent(content);
            image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/octet-stream");
            multipart.Add(
                image,
                fieldName,
                "untrusted-name.bin");
        }

        if (addTextField)
            multipart.Add(
                new StringContent("unexpected", Encoding.UTF8),
                "caption");

        if (addSecondFile)
            multipart.Add(
                new ByteArrayContent(_pngContent),
                "image",
                "second.png");

        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image")
        {
            Content = multipart
        };

        if (entityTag is not null)
            request.Headers.TryAddWithoutValidation(
                HeaderNames.IfMatch,
                entityTag);

        return request;
    }
}
