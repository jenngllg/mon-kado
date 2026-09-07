using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using static JennGllg.Fr.MonKado.Back.Api.IntegrationTests.WishlistReportReviewTestData;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistReportReviewAccessIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/events")]
    [InlineData("PUT", "")]
    public async Task SendAsync_WhenCallerLacksLiveAdministratorAccess_RejectsEveryReviewRoute(
        string method,
        string suffix)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var (adminId, ownerId, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        using var anonymous = factory.CreateClient();
        using var owner = ReportedWishlistTestData.CreateClient(
            factory,
            ownerId);
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        var route = Route(
            wishlist.Id,
            report.Id) + suffix;
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var account = await manager.FindByIdAsync(adminId.ToString());
        Assert.NotNull(account);
        Assert.True((await manager.RemoveFromRoleAsync(
                account,
                RoleNames.Admin)).Succeeded);

        // Act
        using var unauthenticated = await SendAsync(
            anonymous,
            method,
            route,
            ct);
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "invalid.jwt");
        using var invalid = await SendAsync(
            anonymous,
            method,
            route,
            ct);
        using var forbiddenOwner = await SendAsync(
            owner,
            method,
            route,
            ct);
        using var revoked = await SendAsync(
            admin,
            method,
            route,
            ct);
        Assert.True((await manager.DeleteAsync(account)).Succeeded);
        using var deleted = await SendAsync(
            admin,
            method,
            route,
            ct);

        // Assert
        await AssertErrorAsync(
            unauthenticated,
            HttpStatusCode.Unauthorized,
            ct);
        await AssertErrorAsync(
            invalid,
            HttpStatusCode.Unauthorized,
            ct);
        await AssertErrorAsync(
            forbiddenOwner,
            HttpStatusCode.Forbidden,
            ct);
        await AssertErrorAsync(
            revoked,
            HttpStatusCode.Forbidden,
            ct);
        await AssertErrorAsync(
            deleted,
            HttpStatusCode.Unauthorized,
            ct);
    }

    [Theory]
    [InlineData(null, "{\"status\":\"upheld\"}", HttpStatusCode.PreconditionRequired)]
    [InlineData("invalid", "{\"status\":\"upheld\"}", HttpStatusCode.BadRequest)]
    [InlineData("current", "{}", HttpStatusCode.BadRequest)]
    [InlineData("current", "{\"status\":null}", HttpStatusCode.BadRequest)]
    [InlineData("current", "{\"status\":\"unknown\"}", HttpStatusCode.BadRequest)]
    [InlineData("current", "{\"status\":1}", HttpStatusCode.BadRequest)]
    [InlineData("current", "{\"status\":\"upheld\",\"reviewNote\":\"\\u0000\"}", HttpStatusCode.BadRequest)]
    public async Task UpdateAsync_WhenPreconditionsOrBodyAreInvalid_DoesNotCreateReview(
        string? etag,
        string body,
        HttpStatusCode expected)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var (adminId, _, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        var route = Route(
            wishlist.Id,
            report.Id);
        using var initial = await client.GetAsync(
            route,
            ct);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            route);
        request.Content = new StringContent(
            body,
            Encoding.UTF8,
            "application/json");

        if (etag is not null)
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                etag == "current" ? initial.Headers.ETag?.Tag : etag);

        // Act
        using var response = await client.SendAsync(
            request,
            ct);
        using var history = await client.GetAsync(
            route + "/events",
            ct);
        var events = await history.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Assert
        await AssertErrorAsync(
            response,
            expected,
            ct);
        Assert.Equal(
            0,
            events
                .GetProperty("totalCount")
                .GetInt32());
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/events")]
    [InlineData("PUT", "")]
    public async Task SendAsync_WhenReportBelongsToAnotherWishlist_ReturnsStructuredNotFound(
        string method,
        string suffix)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var (adminId, ownerId, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        var other = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        using var initial = await client.GetAsync(
            Route(
                wishlist.Id,
                report.Id),
            ct);
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            Route(
                other.Id,
                report.Id) + suffix);
        request.Headers.IfMatch.Add(Assert.IsType<EntityTagHeaderValue>(initial.Headers.ETag));
        request.Content = JsonContent.Create(new
        {
            status = "upheld"
        });

        // Act
        using var response = await client.SendAsync(
            request,
            ct);

        // Assert
        var error = await AssertErrorAsync(
            response,
            HttpStatusCode.NotFound,
            ct);
        Assert.Equal(
            ErrorCodes.WishlistReportNotFound,
            error.ErrorCode);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=0")]
    public async Task GetEventsAsync_WhenPaginationIsInvalid_ReturnsValidationError(string query)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var (adminId, _, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);

        // Act
        using var response = await client.GetAsync(
            Route(
                wishlist.Id,
                report.Id) + "/events" + query,
            ct);

        // Assert
        await AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            ct);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string method,
        string route,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            route);
        request.Content = JsonContent.Create(new
        {
            status = "upheld"
        });

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<ErrorResponse> AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        Assert.Equal(
            expected,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            (int)expected,
            error.StatusCode);
        Assert.NotNull(error.ErrorCode);

        return error;
    }
}
