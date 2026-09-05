using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class MemberReservationHistoryTests
{
    [Fact]
    public async Task GetReservationHistoryAsync_WhenDefaultsAreUsed_ReturnsExactPaginatedContract()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        var history = CreateHistory();
        factory.GiftReservationHistoryService.Page = new GiftReservationHistoryPage
        {
            Items =
            [
                history
            ],
            CurrentPage = 1,
            PageSize = 20,
            TotalCount = 1
        };
        using var client = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/members/current/reservations",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            (
                memberId,
                1,
                20,
                (GiftReservationHistoryStatus?)null),
            Assert.Single(factory.GiftReservationHistoryService.Retrievals));
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken) ??
            throw new InvalidOperationException("The reservation history response is empty.");
        var root = document.RootElement;
        Assert.Equal(
            [
                "items",
                "currentPage",
                "pageSize",
                "totalCount",
                "totalPages",
                "hasPreviousPage",
                "hasNextPage"
            ],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            1,
            root.GetProperty("currentPage").GetInt32());
        Assert.Equal(
            20,
            root.GetProperty("pageSize").GetInt32());
        Assert.Equal(
            1,
            root.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            1,
            root.GetProperty("totalPages").GetInt32());
        Assert.False(root.GetProperty("hasPreviousPage").GetBoolean());
        Assert.False(root.GetProperty("hasNextPage").GetBoolean());
        AssertHistoryContract(
            Assert.Single(root.GetProperty("items").EnumerateArray()),
            history);
    }

    [Fact]
    public async Task GetReservationHistoryAsync_WhenFilterAndPaginationAreProvided_ForwardsValues()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        factory.GiftReservationHistoryService.Page = new GiftReservationHistoryPage
        {
            CurrentPage = 2,
            PageSize = 5,
            TotalCount = 6
        };
        using var client = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/members/current/reservations?page=2&pageSize=5&status=cancelled",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            (
                memberId,
                2,
                5,
                (GiftReservationHistoryStatus?)GiftReservationHistoryStatus.Cancelled),
            Assert.Single(factory.GiftReservationHistoryService.Retrievals));
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken) ??
            throw new InvalidOperationException("The reservation history response is empty.");
        var root = document.RootElement;
        Assert.Empty(root.GetProperty("items").EnumerateArray());
        Assert.Equal(
            2,
            root.GetProperty("totalPages").GetInt32());
        Assert.True(root.GetProperty("hasPreviousPage").GetBoolean());
        Assert.False(root.GetProperty("hasNextPage").GetBoolean());
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("status=Active", "status")]
    [InlineData("status=0", "status")]
    [InlineData("status=unknown", "status")]
    public async Task GetReservationHistoryAsync_WhenQueryIsInvalid_ReturnsStructuredBadRequest(
        string query,
        string expectedPropertyName)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/members/current/reservations?{query}",
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
            validation => validation.PropertyName == expectedPropertyName);
        Assert.Empty(factory.GiftReservationHistoryService.Retrievals);
    }

    [Fact]
    public async Task GetReservationHistoryAsync_WhenPostgreSqlIsUnavailable_ReturnsStructuredServiceUnavailable()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.GiftReservationHistoryService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.GetAsync(
            "/api/v1/members/current/reservations",
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
    public async Task GetReservationHistoryAsync_WhenBearerTokenIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/members/current/reservations",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.GiftReservationHistoryService.Retrievals);
    }

    [Fact]
    public async Task OpenApi_WhenReservationHistoryIsDocumented_ExposesParametersAndResponses()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken) ??
            throw new InvalidOperationException("The OpenAPI document is empty.");
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/members/current/reservations")
            .GetProperty("get");
        Assert.Equal(
            [
                "page",
                "pageSize",
                "status"
            ],
            operation.GetProperty("parameters")
                .EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString()));
        var responses = operation.GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("401", out _));
        Assert.True(responses.TryGetProperty("403", out _));
        Assert.True(responses.TryGetProperty("500", out _));
        Assert.True(responses.TryGetProperty("503", out _));
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

    private static GiftReservationHistoryDetails CreateHistory()
    {
        return new GiftReservationHistoryDetails
        {
            Id = Guid.CreateVersion7(),
            WishlistId = Guid.CreateVersion7(),
            WishlistName = "Birthday",
            WishId = Guid.CreateVersion7(),
            WishName = "Book",
            ShareLinkId = Guid.CreateVersion7(),
            Quantity = 2,
            Status = GiftReservationHistoryStatus.Active,
            CreatedAt = new DateTime(
                2026,
                9,
                5,
                10,
                0,
                0,
                DateTimeKind.Utc),
            LastActivityAt = new DateTime(
                2026,
                9,
                5,
                11,
                0,
                0,
                DateTimeKind.Utc)
        };
    }

    private static void AssertHistoryContract(
        JsonElement element,
        GiftReservationHistoryDetails expected)
    {
        Assert.Equal(
            [
                "id",
                "wishlistId",
                "wishlistName",
                "wishId",
                "wishName",
                "shareLinkId",
                "quantity",
                "status",
                "createdAt",
                "lastActivityAt",
                "endedAt"
            ],
            element.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            expected.Id,
            element.GetProperty("id").GetGuid());
        Assert.Equal(
            expected.WishlistId,
            element.GetProperty("wishlistId").GetGuid());
        Assert.Equal(
            expected.WishlistName,
            element.GetProperty("wishlistName").GetString());
        Assert.Equal(
            expected.WishId,
            element.GetProperty("wishId").GetGuid());
        Assert.Equal(
            expected.WishName,
            element.GetProperty("wishName").GetString());
        Assert.Equal(
            expected.ShareLinkId,
            element.GetProperty("shareLinkId").GetGuid());
        Assert.Equal(
            expected.Quantity,
            element.GetProperty("quantity").GetInt32());
        Assert.Equal(
            "active",
            element.GetProperty("status").GetString());
        Assert.Equal(
            expected.CreatedAt,
            element.GetProperty("createdAt").GetDateTime());
        Assert.Equal(
            expected.LastActivityAt,
            element.GetProperty("lastActivityAt").GetDateTime());
        Assert.Equal(
            JsonValueKind.Null,
            element.GetProperty("endedAt").ValueKind);
    }
}
