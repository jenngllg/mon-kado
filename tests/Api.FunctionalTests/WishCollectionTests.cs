using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishCollectionTests
{
    [Fact]
    public async Task GetCollectionAsync_WhenWishesExist_ReturnsExactOrderedContractAndEntityTags()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var firstWish = CreateDetails(
            wishlistId,
            1,
            42);
        var secondWish = CreateDetails(
            wishlistId,
            3,
            43);
        factory.WishService.Wishes[(wishlistId, secondWish.Id)] = secondWish;
        factory.WishService.Wishes[(wishlistId, firstWish.Id)] = firstWish;
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"00000054\"",
            response.Headers.ETag?.Tag);
        Assert.Equal(
            [(ownerId, wishlistId)],
            factory.WishService.CollectionRetrievals);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The collection response is empty.");
        Assert.Equal(
            ["wishes"],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name));
        var wishes = document.RootElement
            .GetProperty("wishes")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            [firstWish.Id, secondWish.Id],
            wishes.Select(wish => wish.GetProperty("id").GetGuid()));
        Assert.Equal(
            "\"0000002a\"",
            wishes[0].GetProperty("entityTag").GetString());
        Assert.Equal(
            "\"0000002b\"",
            wishes[1].GetProperty("entityTag").GetString());
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Wishes retrieved from wishlist {wishlistId} for member {ownerId}",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCollectionAsync_WhenWishlistIsEmpty_ReturnsEmptyCollectionWithEntityTag()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Empty(body.GetProperty("wishes").EnumerateArray());
        Assert.Equal(
            "\"00000054\"",
            response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task ReorderAsync_WhenRequestIsValid_ReturnsExactCompleteOrderAndHeaders()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var firstWish = CreateDetails(
            wishlistId,
            1,
            42);
        var secondWish = CreateDetails(
            wishlistId,
            3,
            43);
        factory.WishService.Wishes[(wishlistId, firstWish.Id)] = firstWish;
        factory.WishService.Wishes[(wishlistId, secondWish.Id)] = secondWish;
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await PatchAsync(
            client,
            wishlistId,
            new
            {
                wishIds = new[]
                {
                    secondWish.Id,
                    firstWish.Id
                }
            },
            "\"00000054\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"00000055\"",
            response.Headers.ETag?.Tag);
        var reorder = Assert.Single(factory.WishService.Reorders);
        Assert.Equal(
            ownerId,
            reorder.OwnerId);
        Assert.Equal(
            wishlistId,
            reorder.WishlistId);
        Assert.Equal(
            [secondWish.Id, firstWish.Id],
            reorder.WishIds);
        Assert.Equal(
            84u,
            reorder.ExpectedVersion);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The reorder response is empty.");
        var wishes = document.RootElement
            .GetProperty("wishes")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            ["id", "position", "entityTag"],
            wishes[0]
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            [secondWish.Id, firstWish.Id],
            wishes.Select(wish => wish.GetProperty("id").GetGuid()));
        Assert.Equal(
            [1L, 3L],
            wishes.Select(wish => wish.GetProperty("position").GetInt64()));
        Assert.Equal(
            ["\"00000064\"", "\"00000065\""],
            wishes.Select(wish => wish.GetProperty("entityTag").GetString()));
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Wishes reordered in wishlist {wishlistId} for member {ownerId}",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(WishOrderConflictException), HttpStatusCode.Conflict, "WISH_ORDER_CONFLICT")]
    [InlineData(typeof(WishOrderVersionConflictException), HttpStatusCode.PreconditionFailed, "WISH_ORDER_VERSION_CONFLICT")]
    public async Task ReorderAsync_WhenServiceRejectsRequest_ReturnsStructuredError(
        Type exceptionType,
        HttpStatusCode expectedStatusCode,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishService.Exception = (Exception)(Activator.CreateInstance(exceptionType)
            ?? throw new InvalidOperationException("The expected exception could not be created."));
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await PatchAsync(
            client,
            wishlistId,
            new
            {
                wishIds = Array.Empty<Guid>()
            },
            "\"00000054\"");
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatusCode,
            response.StatusCode);
        Assert.Equal(
            expectedErrorCode,
            error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task CreateAsync_WhenWishLimitIsReached_ReturnsConflict()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishService.Exception = new WishLimitReachedException();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes",
            new
            {
                name = "Cadeau"
            },
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        Assert.Equal(
            "WISH_LIMIT_REACHED",
            error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ReorderAsync_WhenIfMatchIsMissing_ReturnsPreconditionRequired()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await PatchAsync(
            client,
            Guid.CreateVersion7(),
            new
            {
                wishIds = Array.Empty<Guid>()
            },
            null);

        // Assert
        Assert.Equal(
            (HttpStatusCode)428,
            response.StatusCode);
        Assert.Empty(factory.WishService.Reorders);
    }

    [Fact]
    public async Task ReorderAsync_WhenWishIdsAreNull_ReturnsBadRequest()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await PatchAsync(
            client,
            Guid.CreateVersion7(),
            new
            {
                wishIds = (Guid[]?)null
            },
            "\"00000054\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.WishService.Reorders);
    }

    [Fact]
    public async Task ReorderAsync_WhenBodyExceedsMaximumSize_ReturnsPayloadTooLarge()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var content = new StringContent(
            "{\"wishIds\":[],\"padding\":\"" + new string(
                'a',
                65 * 1024) + "\"}",
            Encoding.UTF8,
            "application/json");
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            "\"00000054\"");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Empty(factory.WishService.Reorders);
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

    private static async Task<HttpResponseMessage> PatchAsync(
        HttpClient client,
        Guid wishlistId,
        object body,
        string? entityTag)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/wishlists/{wishlistId}/wishes")
        {
            Content = JsonContent.Create(body)
        };

        if (entityTag is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static WishDetails CreateDetails(
        Guid wishlistId,
        long position,
        uint version)
    {
        return new WishDetails(
            Guid.CreateVersion7(),
            wishlistId,
            $"Cadeau {position}",
            null,
            null,
            null,
            position,
            new DateTime(
                2026,
                8,
                25,
                12,
                0,
                0,
                DateTimeKind.Utc),
            null,
            version);
    }
}
