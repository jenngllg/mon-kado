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
public class WishlistReportReviewLifecycleIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task GetPageAsync_WhenStatusesAndReasonsAreCombined_FiltersBeforeGroupingAndRetainsClosedReportAccess()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        var clock = new FixedTimeProvider(new DateTimeOffset(
                2026,
                9,
                7,
                10,
                0,
                0,
                TimeSpan.Zero));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var (adminId, ownerId, first, firstReport) = await PrepareAsync(
            factory,
            ct);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var pending = await ReportedWishlistTestData.AddReportAsync(
            factory,
            first.Id,
            WishlistReportReason.SpamOrScam,
            ct);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var second = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            ct);
        var dismissed = await ReportedWishlistTestData.AddReportAsync(
            factory,
            second.Id,
            WishlistReportReason.Other,
            ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var tracked = await context.Wishlists.SingleAsync(
            wishlist => wishlist.Id == first.Id,
            ct);
        tracked.Moderate(
            true,
            "Private suspension",
            clock
                .GetUtcNow()
                .UtcDateTime);
        await context.SaveChangesAsync(ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        using var firstRead = await client.GetAsync(
            Route(
                first.Id,
                firstReport.Id),
            ct);
        using var secondRead = await client.GetAsync(
            Route(
                second.Id,
                dismissed.Id),
            ct);
        using var upheldResponse = await UpdateAsync(
            client,
            Route(
                first.Id,
                firstReport.Id),
            Assert.IsType<string>(firstRead.Headers.ETag?.Tag),
            "upheld",
            null,
            ct);
        using var dismissedResponse = await UpdateAsync(
            client,
            Route(
                second.Id,
                dismissed.Id),
            Assert.IsType<string>(secondRead.Headers.ETag?.Tag),
            "dismissed",
            null,
            ct);
        Assert.Equal(
            HttpStatusCode.OK,
            upheldResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            dismissedResponse.StatusCode);
        const string collection = "/api/v1/admin/reported-wishlists";

        // Act
        var queue = await ReadAsync(
            client,
            collection,
            ct);
        var all = await ReadAsync(
            client,
            collection + "?status=all&reason=other",
            ct);
        var upheld = await ReadAsync(
            client,
            collection + "?status=upheld&reason=other&isSuspended=true",
            ct);
        var rejected = await ReadAsync(
            client,
            collection + "?status=dismissed&isSuspended=false",
            ct);
        var pendingReports = await ReadAsync(
            client,
            $"{collection}/{first.Id}/reports",
            ct);
        var allReports = await ReadAsync(
            client,
            $"{collection}/{first.Id}/reports?status=all",
            ct);
        var upheldReports = await ReadAsync(
            client,
            $"{collection}/{first.Id}/reports?status=upheld",
            ct);
        var dismissedReports = await ReadAsync(
            client,
            $"{collection}/{second.Id}/reports?status=dismissed",
            ct);
        var closedPending = await ReadAsync(
            client,
            $"{collection}/{second.Id}/reports",
            ct);

        // Assert
        var group = Assert.Single(queue
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            first.Id,
            group
                .GetProperty("wishlistId")
                .GetGuid());
        Assert.Equal(
            1,
            group
                .GetProperty("reportCount")
                .GetInt32());
        Assert.Equal(
            pending.CreatedAt,
            group
                .GetProperty("lastReportedAt")
                .GetDateTime());
        Assert.Equal(
            [
                second.Id,
                first.Id
            ],
            all
                .GetProperty("items")
                .EnumerateArray()
                .Select(value => value
                    .GetProperty("wishlistId")
                    .GetGuid()));
        Assert.Equal(
            firstReport.CreatedAt,
            Assert
                .Single(upheld
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("lastReportedAt")
                .GetDateTime());
        Assert.Equal(
            second.Id,
            Assert
                .Single(rejected
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("wishlistId")
                .GetGuid());
        Assert.Equal(
            pending.Id,
            Assert
                .Single(pendingReports
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            2,
            allReports
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            firstReport.Id,
            Assert
                .Single(upheldReports
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            dismissed.Id,
            Assert
                .Single(dismissedReports
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.Empty(closedPending
                .GetProperty("items")
                .EnumerateArray());
    }

    [Fact]
    public async Task GetEventsAsync_WhenAdministratorAndThenWishlistAreDeleted_RetainsAnonymousDecisionsUntilCascadeDeletion()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        var clock = new FixedTimeProvider(new DateTimeOffset(
                2026,
                9,
                7,
                10,
                0,
                0,
                TimeSpan.Zero));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var (adminId, ownerId, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        var otherAdminId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        using var remainingAdmin = ReportedWishlistTestData.CreateClient(
            factory,
            otherAdminId);
        using var owner = ReportedWishlistTestData.CreateClient(
            factory,
            ownerId);
        var route = Route(
            wishlist.Id,
            report.Id);
        using var initial = await client.GetAsync(
            route,
            ct);
        using var first = await UpdateAsync(
            client,
            route,
            Assert.IsType<string>(initial.Headers.ETag?.Tag),
            "upheld",
            "Private review",
            ct);
        using var second = await UpdateAsync(
            client,
            route,
            Assert.IsType<string>(first.Headers.ETag?.Tag),
            "dismissed",
            "Corrected review",
            ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var administrator = await manager.FindByIdAsync(adminId.ToString());
        Assert.NotNull(administrator);

        // Act
        Assert.True((await manager.DeleteAsync(administrator)).Succeeded);
        var current = await ReadAsync(
            remainingAdmin,
            route,
            ct);
        var firstPage = await ReadAsync(
            remainingAdmin,
            route + "/events?pageSize=1",
            ct);
        var secondPage = await ReadAsync(
            remainingAdmin,
            route + "/events?pageSize=1&page=2",
            ct);
        var outside = await ReadAsync(
            remainingAdmin,
            route + "/events?pageSize=100&page=2147483647",
            ct);
        using var list = await owner.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}",
            ct);
        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlist.Id}");
        deleteRequest.Headers.IfMatch.Add(Assert.IsType<EntityTagHeaderValue>(list.Headers.ETag));
        using var deleted = await owner.SendAsync(
            deleteRequest,
            ct);
        using var missingHistory = await remainingAdmin.GetAsync(
            route + "/events",
            ct);
        using var missingReport = await remainingAdmin.GetAsync(
            route,
            ct);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        // Assert
        Assert.Equal(
            JsonValueKind.Null,
            current
                .GetProperty("reviewedByAdministratorId")
                .ValueKind);
        Assert.Equal(
            "Corrected review",
            current
                .GetProperty("reviewNote")
                .GetString());
        var latest = Assert.Single(firstPage
                .GetProperty("items")
                .EnumerateArray());
        var previous = Assert.Single(secondPage
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            "dismissed",
            latest
                .GetProperty("status")
                .GetString());
        Assert.Equal(
            "upheld",
            previous
                .GetProperty("status")
                .GetString());
        Assert.Equal(
            latest
                .GetProperty("occurredAt")
                .GetDateTime(),
            previous
                .GetProperty("occurredAt")
                .GetDateTime());
        Assert.Equal(
            JsonValueKind.Null,
            latest
                .GetProperty("administratorId")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            previous
                .GetProperty("administratorId")
                .ValueKind);
        Assert.Equal(
            2,
            firstPage
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            2147483647,
            outside
                .GetProperty("currentPage")
                .GetInt32());
        Assert.Equal(
            2,
            outside
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Empty(outside
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            HttpStatusCode.NoContent,
            deleted.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            missingHistory.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            missingReport.StatusCode);
        Assert.Empty(await context.WishlistReportReviewEvents
                .AsNoTracking()
                .ToArrayAsync(ct));
    }

    private static async Task<JsonElement> ReadAsync(
        HttpClient client,
        string route,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            route,
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }
}
