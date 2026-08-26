using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistShareLinkTests
{
    [Fact]
    public async Task CreateAsync_WhenBearerIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/share-link",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.WishlistShareService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenWishlistIsOwned_ReturnsCopyableShareLink()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.PostAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        var creation = Assert.Single(factory.WishlistShareService.Creations);
        Assert.Equal(
            ownerId,
            creation.OwnerId);
        Assert.Equal(
            wishlistId,
            creation.WishlistId);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The share-link response is empty.");
        Assert.Equal(
            [
                "id",
                "shareUrl",
                "createdAt",
                "updatedAt"
            ],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            $"http://localhost:5173/#/shared-wishlists/{creation.Id:N}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            document.RootElement.GetProperty("shareUrl").GetString());
        Assert.Equal(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task RotateAsync_WhenVersionMatches_ReturnsNewSecretAndEntityTag()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        await factory.WishlistShareService.CreateAsync(
            shareLinkId,
            ownerId,
            wishlistId,
            TestContext.Current.CancellationToken);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/share-link");
        request.Headers.TryAddWithoutValidation(
            HeaderNames.IfMatch,
            "\"0000002a\"");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "\"0000002b\"",
            response.Headers.ETag?.Tag);
        Assert.Equal(
            [(ownerId, wishlistId, 42u)],
            factory.WishlistShareService.Rotations);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(
            ".BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            content,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOwnerLinkAsync_WhenShareLinkExists_ReturnsSameCopyableUrl()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        await factory.WishlistShareService.CreateAsync(
            shareLinkId,
            ownerId,
            wishlistId,
            TestContext.Current.CancellationToken);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        Assert.Equal(
            [(ownerId, wishlistId)],
            factory.WishlistShareService.Retrievals);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The share-link response is empty.");
        Assert.Equal(
            shareLinkId,
            document.RootElement.GetProperty("id").GetGuid());
        Assert.Contains(
            ".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            document.RootElement.GetProperty("shareUrl").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOwnerLinkAsync_WhenShareLinkDoesNotExist_ReturnsStructuredNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            "WISHLIST_SHARE_LINK_NOT_FOUND",
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task CreateAsync_WhenWishlistIsNotOwned_ReturnsNonDisclosingNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.Access = WishlistAccess.NotOwned;
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.PostAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        Assert.Empty(factory.WishlistShareService.Creations);
    }

    [Fact]
    public async Task DeleteAsync_WhenVersionMatches_RevokesShareLink()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        await factory.WishlistShareService.CreateAsync(
            Guid.CreateVersion7(),
            ownerId,
            wishlistId,
            TestContext.Current.CancellationToken);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlistId}/share-link");
        request.Headers.TryAddWithoutValidation(
            HeaderNames.IfMatch,
            "\"0000002a\"");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.Equal(
            [(ownerId, wishlistId, 42u)],
            factory.WishlistShareService.Deletions);
        Assert.Empty(factory.WishlistShareService.ShareLinks);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task MutateAsync_WhenIfMatchIsMissing_ReturnsPreconditionRequired(string method)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/v1/wishlists/{wishlistId}/share-link");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            (HttpStatusCode)StatusCodes.Status428PreconditionRequired,
            response.StatusCode);
        Assert.Empty(factory.WishlistShareService.Rotations);
        Assert.Empty(factory.WishlistShareService.Deletions);
    }

    [Fact]
    public async Task GetAsync_WhenShareTokenIsValid_ReturnsOnlyPublicContent()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var shareLinkId = Guid.CreateVersion7();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            "public-secret");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "noindex, nofollow, noarchive",
            response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal(
            [(shareLinkId, "public-secret")],
            factory.WishlistShareService.PublicRetrievals);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The shared-wishlist response is empty.");
        Assert.Equal(
            [
                "id",
                "ownerDisplayName",
                "name",
                "occasion",
                "eventDate",
                "message",
                "wishes"
            ],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name));
        var wish = document.RootElement.GetProperty("wishes")[0];
        Assert.Equal(
            [
                "id",
                "name",
                "url",
                "price"
            ],
            wish
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.False(wish.TryGetProperty(
            "note",
            out _));
        Assert.Equal(
            "Jenn",
            document.RootElement.GetProperty("ownerDisplayName").GetString());
    }

    [Fact]
    public async Task GetAsync_WhenShareTokenIsInvalid_ReturnsNonDisclosingNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistShareService.SharedWishlist = null;
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/shared-wishlists/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            "SHARED_WISHLIST_NOT_FOUND",
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public Task CreateAsync_WhenShareLinkAlreadyExists_ReturnsStructuredConflict()
    {
        return AssertMutationErrorAsync(
            new WishlistShareLinkAlreadyExistsException(),
            HttpMethod.Post,
            HttpStatusCode.Conflict,
            "WISHLIST_SHARE_LINK_ALREADY_EXISTS",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task RotateAsync_WhenVersionConflicts_ReturnsStructuredPreconditionFailure()
    {
        return AssertMutationErrorAsync(
            new WishlistShareLinkVersionConflictException(),
            HttpMethod.Put,
            HttpStatusCode.PreconditionFailed,
            "WISHLIST_SHARE_LINK_VERSION_CONFLICT",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetAsync_WhenPublicRateLimitIsExceeded_ReturnsStructuredTooManyRequests()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var shareLinkId = Guid.CreateVersion7();

        // Act
        var response = await SendPublicRequestAsync(
            client,
            shareLinkId,
            TestContext.Current.CancellationToken);

        try
        {
            for (var requestNumber = 1;
                requestNumber <= AuthenticationRateLimitingExtensions.SharedWishlistPermitLimit;
                requestNumber++)
            {
                response.Dispose();
                response = await SendPublicRequestAsync(
                    client,
                    shareLinkId,
                    TestContext.Current.CancellationToken);
            }

            // Assert
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
                TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The error response is empty.");
            Assert.Equal(
                "REQUEST_RATE_LIMIT_EXCEEDED",
                document.RootElement.GetProperty("errorCode").GetString());
        }
        finally
        {
            response.Dispose();
        }
    }

    private static async Task AssertMutationErrorAsync(
        Exception exception,
        HttpMethod method,
        HttpStatusCode expectedStatus,
        string expectedErrorCode,
        CancellationToken cancellationToken)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        factory.WishlistShareService.Exception = exception;
        using var request = new HttpRequestMessage(
            method,
            $"/api/v1/wishlists/{wishlistId}/share-link");

        if (method == HttpMethod.Put)
        {
            request.Headers.TryAddWithoutValidation(
                HeaderNames.IfMatch,
                "\"0000002a\"");
        }

        // Act
        using var response = await client.SendAsync(
            request,
            cancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            expectedErrorCode,
            document.RootElement.GetProperty("errorCode").GetString());
    }

    private static async Task<HttpResponseMessage> SendPublicRequestAsync(
        HttpClient client,
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            "public-secret");

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static HttpClient CreateAuthorizedClient(
        RegistrationApiFactory factory,
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
}
