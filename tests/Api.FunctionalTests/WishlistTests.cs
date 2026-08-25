using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistTests
{
    [Fact]
    public async Task GetAllAsync_WhenMemberOwnsWishlists_ReturnsExactOrderedCollection()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        var recentWishlist = CreateWishlistDetails(
            "Liste récente",
            WishlistOccasion.Birthday,
            new DateOnly(
                2099,
                9,
                24),
            "Merci",
            new DateTime(
                2026,
                8,
                25,
                12,
                0,
                0,
                DateTimeKind.Utc));
        var olderWishlist = CreateWishlistDetails(
            "Liste passée",
            WishlistOccasion.Other,
            new DateOnly(
                2020,
                1,
                1),
            null,
            new DateTime(
                2026,
                8,
                24,
                12,
                0,
                0,
                DateTimeKind.Utc));
        factory.WishlistService.OwnedWishlists.AddRange(
        [
            recentWishlist,
            olderWishlist
        ]);
        using var client = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/wishlists",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.ETag);
        Assert.Equal(
            [memberId],
            factory.WishlistService.OwnerRetrievals);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The wishlist collection response is empty.");
        var wishlists = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(
            2,
            wishlists.Length);
        Assert.Equal(
            [
                "id",
                "name",
                "occasion",
                "eventDate",
                "message",
                "createdAt",
                "updatedAt"
            ],
            wishlists[0]
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            recentWishlist.Id,
            wishlists[0].GetProperty("id").GetGuid());
        Assert.Equal(
            "Liste récente",
            wishlists[0].GetProperty("name").GetString());
        Assert.Equal(
            olderWishlist.Id,
            wishlists[1].GetProperty("id").GetGuid());
        Assert.Equal(
            "2020-01-01",
            wishlists[1].GetProperty("eventDate").GetString());
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Wishlists retrieved for member {memberId}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "Liste récente",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAllAsync_WhenMemberOwnsNoWishlist_ReturnsEmptyCollection()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/wishlists",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var wishlists = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Empty(wishlists.EnumerateArray());
        Assert.Equal(
            [memberId],
            factory.WishlistService.OwnerRetrievals);
    }

    [Fact]
    public async Task GetAllAsync_WhenBearerTokenIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/wishlists",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.WishlistService.OwnerRetrievals);
    }

    [Fact]
    public async Task GetAllAsync_WhenMemberWasDeleted_ReturnsUnauthorizedAndDeletesRefreshCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.MemberExists = false;
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.GetAsync(
            "/api/v1/wishlists",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAllAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.GetAsync(
            "/api/v1/wishlists",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenBearerTokenIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest();

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.WishlistService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenRequestIsValid_ReturnsExactWishlistContractAndHeaders()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = CreateRequest();

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        var creation = Assert.Single(factory.WishlistService.Creations);
        Assert.Equal(
            ownerId,
            creation.OwnerId);
        Assert.Equal(
            7,
            creation.Id.Version);
        Assert.Equal(
            "Liste de Léa",
            creation.Name);
        Assert.Equal(
            "LISTE DE LÉA",
            creation.NormalizedName);
        Assert.Equal(
            "/api/v1/wishlists/" + creation.Id,
            response.Headers.Location?.AbsolutePath);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The wishlist response is empty.");
        var properties = document.RootElement.EnumerateObject().ToArray();
        Assert.Equal(
            [
                "id",
                "name",
                "occasion",
                "eventDate",
                "message",
                "createdAt",
                "updatedAt"
            ],
            properties.Select(property => property.Name));
        Assert.Equal(
            creation.Id,
            document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(
            "Liste de Léa",
            document.RootElement.GetProperty("name").GetString());
        Assert.Equal(
            "birthday",
            document.RootElement.GetProperty("occasion").GetString());
        Assert.Equal(
            "2099-09-24",
            document.RootElement.GetProperty("eventDate").GetString());
        Assert.Equal(
            "Merci d’être là",
            document.RootElement.GetProperty("message").GetString());
        Assert.Equal(
            DateTimeKind.Utc,
            document.RootElement.GetProperty("createdAt").GetDateTime().Kind);
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("updatedAt").ValueKind);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Wishlist {creation.Id} created for member {ownerId}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "Liste de Léa",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "Merci d’être là",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenCurrentMemberOwnsWishlist_ReturnsCreatedWishlist()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var createRequest = CreateRequest();
        using var createResponse = await client.SendAsync(
            createRequest,
            TestContext.Current.CancellationToken);
        var location = createResponse.Headers.Location
            ?? throw new InvalidOperationException("The wishlist location is missing.");

        // Act
        using var response = await client.GetAsync(
            location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        var creation = Assert.Single(factory.WishlistService.Creations);
        Assert.Equal(
            [(ownerId, creation.Id)],
            factory.WishlistService.Accesses);
        Assert.Equal(
            [creation.Id],
            factory.WishlistService.Retrievals);
        var wishlist = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            creation.Id,
            wishlist.GetProperty("id").GetGuid());
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Wishlist {creation.Id} retrieved for member {ownerId}",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"name\":null,\"occasion\":\"birthday\"}", "name")]
    [InlineData("{\"name\":\"Liste\",\"occasion\":null}", "occasion")]
    [InlineData("{\"name\":\"Liste\",\"occasion\":\"birthday\",\"eventDate\":\"2000-01-01\"}", "eventDate")]
    public async Task CreateAsync_WhenRequestIsInvalid_ReturnsValidationError(
        string json,
        string propertyName)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/wishlists")
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error.ErrorCode);
        Assert.Contains(
            error.ValidationErrors ?? [],
            validationError => validationError.PropertyName == propertyName);
        Assert.Empty(factory.WishlistService.Creations);
    }

    [Theory]
    [InlineData("{\"name\":\"Liste\",\"occasion\":\"unknown\"}")]
    [InlineData("{\"name\":\"Liste\",\"occasion\":42}")]
    public async Task CreateAsync_WhenOccasionJsonIsUnknown_ReturnsBadRequest(string json)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/wishlists")
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error.ErrorCode);
        Assert.Empty(factory.WishlistService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenMemberWasDeleted_ReturnsUnauthorizedAndDeletesRefreshCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.MemberExists = false;
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest();

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_WhenNameAlreadyExists_ReturnsConflict()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.Exception = new WishlistNameAlreadyExistsException();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest();

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistNameAlreadyExists,
            error.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest();

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error.ErrorCode);
    }

    [Theory]
    [InlineData(WishlistAccess.NotOwned, HttpStatusCode.NotFound, ErrorCodes.WishlistNotFound)]
    [InlineData(WishlistAccess.MemberNotFound, HttpStatusCode.Unauthorized, ErrorCodes.AccountAuthenticationSessionInvalid)]
    public async Task GetAsync_WhenAccessIsRejected_ReturnsPrivateResourceError(
        WishlistAccess access,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.Access = access;
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        var wishlistId = Guid.CreateVersion7();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            expectedErrorCode,
            error.ErrorCode);
        Assert.Equal(
            [wishlistId],
            factory.WishlistService.Accesses.Select(call => call.WishlistId));
        Assert.Empty(factory.WishlistService.Retrievals);
    }

    [Fact]
    public async Task GetAsync_WhenWishlistDisappearsAfterAuthorization_ReturnsNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        var wishlistId = Guid.CreateVersion7();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistNotFound,
            error.ErrorCode);
        Assert.Equal(
            [wishlistId],
            factory.WishlistService.Retrievals);
    }

    [Fact]
    public async Task GetAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        var wishlistId = Guid.CreateVersion7();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error.ErrorCode);
        Assert.Empty(factory.WishlistService.Retrievals);
    }

    [Fact]
    public async Task CreateAsync_WhenContentTypeIsNotJson_ReturnsUnsupportedMediaType()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/wishlists")
        {
            Content = new StringContent(
                "name=Liste",
                Encoding.UTF8,
                "text/plain")
        };

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        Assert.Empty(factory.WishlistService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenBodyExceedsMaximumSize_ReturnsPayloadTooLarge()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/wishlists")
        {
            Content = new StringContent(
                "{\"name\":\"" + new string(
                    'a',
                    5 * 1024) + "\",\"occasion\":\"other\"}",
                Encoding.UTF8,
                "application/json")
        };

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Empty(factory.WishlistService.Creations);
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

    private static HttpRequestMessage CreateRequest()
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/wishlists")
        {
            Content = JsonContent.Create(new
            {
                name = "  Liste de Le\u0301a  ",
                occasion = "birthday",
                eventDate = "2099-09-24",
                message = "  Merci d’être là  "
            })
        };
    }

    private static WishlistDetails CreateWishlistDetails(
        string name,
        WishlistOccasion occasion,
        DateOnly? eventDate,
        string? message,
        DateTime createdAt)
    {
        return new WishlistDetails(
            Guid.CreateVersion7(),
            name,
            occasion,
            eventDate,
            message,
            createdAt,
            null,
            42);
    }
}
