using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using static JennGllg.Fr.MonKado.Back.Api.IntegrationTests.WishlistReportReviewTestData;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistReportReviewIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task UpdateAsync_WhenReviewIsCorrectedAndReopened_PreservesHistoryWithoutModerationOrNotifications()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var (adminId, _, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            adminId,
            TestContext.Current.CancellationToken);
        var route = Route(
            wishlist.Id,
            report.Id);
        using var initial = await client.GetAsync(
            route,
            ct);
        var firstTag = Assert.IsType<string>(initial.Headers.ETag?.Tag);
        var initialBody = await initial.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Act
        using var upheld = await UpdateAsync(
            client,
            route,
            firstTag,
            "upheld",
            "  Private review  ",
            ct);
        var upheldTag = Assert.IsType<string>(upheld.Headers.ETag?.Tag);
        var upheldBody = await upheld.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var unchanged = await UpdateAsync(
            client,
            route,
            upheldTag,
            "upheld",
            "Private review",
            ct);
        using var stale = await UpdateAsync(
            client,
            route,
            firstTag,
            "upheld",
            "Private review",
            ct);
        using var edited = await UpdateAsync(
            client,
            route,
            upheldTag,
            "upheld",
            "Corrected private note",
            ct);
        using var rejected = await UpdateAsync(
            client,
            route,
            Assert.IsType<string>(edited.Headers.ETag?.Tag),
            "dismissed",
            null,
            ct);
        using var closedQueue = await client.GetAsync(
            "/api/v1/admin/reported-wishlists",
            ct);
        var closedBody = await closedQueue.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var closedDetails = await client.GetAsync(
            $"/api/v1/admin/reported-wishlists/{wishlist.Id}",
            ct);
        using var reopened = await UpdateAsync(
            client,
            route,
            Assert.IsType<string>(rejected.Headers.ETag?.Tag),
            "pending",
            null,
            ct);
        using var openQueue = await client.GetAsync(
            "/api/v1/admin/reported-wishlists",
            ct);
        var openBody = await openQueue.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var history = await client.GetAsync(
            route + "/events",
            ct);
        var events = await history.Content.ReadFromJsonAsync<JsonElement>(ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var unchangedWishlist = await context.Wishlists
            .AsNoTracking()
            .SingleAsync(
            value => value.Id == wishlist.Id,
            ct);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            initial.StatusCode);
        Assert.Equal(
            "pending",
            initialBody
                .GetProperty("status")
                .GetString());
        Assert.Equal(
            JsonValueKind.Null,
            initialBody
                .GetProperty("reviewNote")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            initialBody
                .GetProperty("reviewedAt")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            initialBody
                .GetProperty("reviewedByAdministratorId")
                .ValueKind);
        Assert.Equal(
            HttpStatusCode.OK,
            upheld.StatusCode);
        Assert.NotEqual(
            firstTag,
            upheldTag);
        Assert.Equal(
            upheldTag,
            unchanged.Headers.ETag?.Tag);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            stale.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            reopened.StatusCode);
        Assert.True(upheld.Headers.CacheControl?.NoStore);
        Assert.True(history.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "Private review",
            upheldBody
                .GetProperty("reviewNote")
                .GetString());
        Assert.Equal(
            adminId,
            upheldBody
                .GetProperty("reviewedByAdministratorId")
                .GetGuid());
        Assert.Equal(
            report.Details,
            upheldBody
                .GetProperty("details")
                .GetString());
        Assert.Equal(
            0,
            closedBody
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            HttpStatusCode.OK,
            closedDetails.StatusCode);
        Assert.Equal(
            1,
            openBody
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            4,
            events
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            [
                "pending",
                "dismissed",
                "upheld",
                "upheld"
            ],
            events
                .GetProperty("items")
                .EnumerateArray()
                .Select(value => value
                    .GetProperty("status")
                    .GetString()));
        Assert.Equal(
            [
                "dismissed",
                "upheld",
                "upheld",
                "pending"
            ],
            events
                .GetProperty("items")
                .EnumerateArray()
                .Select(value => value
                    .GetProperty("previousStatus")
                    .GetString()));
        Assert.Equal(
            wishlist.Version,
            unchangedWishlist.Version);
        Assert.False(unchangedWishlist.IsSuspended);
        Assert.Empty(await context.WishlistModerationEmails.ToArrayAsync(ct));
        Assert.Empty(await context.AuthenticationEmailOutboxMessages.ToArrayAsync(ct));
        Assert.Equal(
            [
                "createdAt",
                "details",
                "id",
                "reason",
                "reviewedAt",
                "reviewedByAdministratorId",
                "reviewNote",
                "status"
            ],
            upheldBody
                .EnumerateObject()
                .Select(value => value.Name)
                .Order());
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public async Task UpdateAsync_WhenCommitAcknowledgementFails_ReconcilesOnlyDurableReview(string failure)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        var interceptor = new WishlistModerationCommitInterceptor();
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.ConfigureDbContext<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor)));
        var (adminId, _, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            adminId,
            TestContext.Current.CancellationToken);
        var route = Route(
            wishlist.Id,
            report.Id);
        using var initial = await client.GetAsync(
            route,
            ct);
        var tag = Assert.IsType<string>(initial.Headers.ETag?.Tag);

        if (failure == "before")
            interceptor.ArmBeforeCommit();
        else
            interceptor.Arm();

        // Act
        using var response = await UpdateAsync(
            client,
            route,
            tag,
            "dismissed",
            null,
            ct);
        using var current = await client.GetAsync(
            route,
            ct);
        var currentBody = await current.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var history = await client.GetAsync(
            route + "/events",
            ct);
        var historyBody = await history.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Assert
        Assert.Equal(
            failure == "before" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            failure == "before" ? "pending" : "dismissed",
            currentBody
                .GetProperty("status")
                .GetString());
        Assert.Equal(
            failure == "before" ? 0 : 1,
            historyBody
                .GetProperty("totalCount")
                .GetInt32());
    }

    [Fact]
    public async Task UpdateAsync_WhenAdministratorsRace_OnlyOneVersionCanWin()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var (adminId, _, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        var otherId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            ct);
        using var first = await AuthenticationTestData.CreateClientAsync(
            factory,
            adminId,
            TestContext.Current.CancellationToken);
        using var second = await AuthenticationTestData.CreateClientAsync(
            factory,
            otherId,
            TestContext.Current.CancellationToken);
        var route = Route(
            wishlist.Id,
            report.Id);
        using var initial = await first.GetAsync(
            route,
            ct);
        var tag = Assert.IsType<string>(initial.Headers.ETag?.Tag);

        // Act
        var responses = await Task.WhenAll(
            UpdateAsync(
                first,
                route,
                tag,
                "upheld",
                null,
                ct),
            UpdateAsync(
                second,
                route,
                tag,
                "dismissed",
                null,
                ct));
        using var winner = Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.OK);
        using var loser = Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        using var history = await first.GetAsync(
            route + "/events",
            ct);
        var body = await history.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Assert
        Assert.Equal(
            1,
            body
                .GetProperty("totalCount")
                .GetInt32());
        var error = await loser.Content.ReadFromJsonAsync<ErrorResponse>(ct);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistReportVersionConflict,
            error.ErrorCode);
    }
}
