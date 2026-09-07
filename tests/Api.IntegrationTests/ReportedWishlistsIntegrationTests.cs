using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class ReportedWishlistsIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Route = "/api/v1/admin/reported-wishlists";
    [Fact]
    public async Task GetPageAsync_WhenReasonsAreFiltered_CountsAndOrdersOnlyMatchingReports()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        var clock = new FixedTimeProvider(new DateTimeOffset(
                2026,
                9,
                7,
                12,
                0,
                0,
                TimeSpan.Zero));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var ownerId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            ct);
        var adminId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            ct);
        var first = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            ct);
        var second = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            ct);
        await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            ct);
        var firstReport = await ReportedWishlistTestData.AddReportAsync(
            factory,
            first.Id,
            WishlistReportReason.Other,
            ct);
        await ReportedWishlistTestData.AddReportAsync(
            factory,
            first.Id,
            WishlistReportReason.Other,
            ct);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var secondReport = await ReportedWishlistTestData.AddReportAsync(
            factory,
            second.Id,
            WishlistReportReason.Other,
            ct);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var latest = await ReportedWishlistTestData.AddReportAsync(
            factory,
            first.Id,
            WishlistReportReason.SpamOrScam,
            ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var wishlist = await db.Wishlists.SingleAsync(
                item => item.Id == first.Id,
                ct);
            wishlist.Moderate(
                true,
                "Private suspension reason",
                clock
                    .GetUtcNow()
                    .UtcDateTime);
            await db.SaveChangesAsync(ct);
        }

        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);

        // Act
        using var unfiltered = await ReadAsync(
            client,
            Route,
            ct);
        using var filtered = await ReadAsync(
            client,
            Route + "?reason=other&pageSize=1",
            ct);
        using var next = await ReadAsync(
            client,
            Route + "?reason=other&pageSize=1&page=2",
            ct);
        using var suspended = await ReadAsync(
            client,
            Route + "?reason=other&isSuspended=true",
            ct);
        using var active = await ReadAsync(
            client,
            Route + "?isSuspended=false",
            ct);
        using var empty = await ReadAsync(
            client,
            Route + "?reason=privacyViolation",
            ct);
        using var outside = await ReadAsync(
            client,
            Route + "?page=2147483647&pageSize=100",
            ct);

        // Assert
        Assert.Equal(
            2,
            unfiltered.RootElement
                .GetProperty("totalCount")
                .GetInt32());
        var allFirst = unfiltered.RootElement.GetProperty("items")[0];
        Assert.Equal(
            first.Id,
            allFirst
                .GetProperty("wishlistId")
                .GetGuid());
        Assert.Equal(
            3,
            allFirst
                .GetProperty("reportCount")
                .GetInt32());
        Assert.Equal(
            latest.CreatedAt,
            allFirst
                .GetProperty("lastReportedAt")
                .GetDateTime());
        var filteredItem = Assert.Single(filtered.RootElement
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            second.Id,
            filteredItem
                .GetProperty("wishlistId")
                .GetGuid());
        Assert.Equal(
            secondReport.CreatedAt,
            filteredItem
                .GetProperty("lastReportedAt")
                .GetDateTime());
        Assert.Equal(
            2,
            filtered.RootElement
                .GetProperty("totalCount")
                .GetInt32());
        Assert.True(filtered.RootElement
                .GetProperty("hasNextPage")
                .GetBoolean());
        var nextItem = Assert.Single(next.RootElement
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            first.Id,
            nextItem
                .GetProperty("wishlistId")
                .GetGuid());
        Assert.Equal(
            2,
            nextItem
                .GetProperty("reportCount")
                .GetInt32());
        Assert.Equal(
            firstReport.CreatedAt,
            nextItem
                .GetProperty("lastReportedAt")
                .GetDateTime());
        Assert.Equal(
            first.Id,
            Assert
                .Single(suspended.RootElement
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("wishlistId")
                .GetGuid());
        Assert.Equal(
            second.Id,
            Assert
                .Single(active.RootElement
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("wishlistId")
                .GetGuid());
        Assert.Equal(
            0,
            empty.RootElement
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Empty(empty.RootElement
                .GetProperty("items")
                .EnumerateArray());
        Assert.Empty(outside.RootElement
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            int.MaxValue,
            outside.RootElement
                .GetProperty("currentPage")
                .GetInt32());
        Assert.Equal(
            2,
            outside.RootElement
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            [
                "isSuspended",
                "lastReportedAt",
                "name",
                "ownerDisplayName",
                "ownerId",
                "reportCount",
                "wishlistId"
            ],
            allFirst
                .EnumerateObject()
                .Select(property => property.Name)
                .Order());
    }

    [Fact]
    public async Task GetReportsAsync_WhenDatesTie_ReturnsDeterministicAnonymousPages()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            new FixedTimeProvider(DateTimeOffset.UtcNow));
        var adminId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            ct);
        var wishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            adminId,
            ct);
        var secondWishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            adminId,
            ct);
        var first = await ReportedWishlistTestData.AddReportAsync(
            factory,
            wishlist.Id,
            WishlistReportReason.Other,
            ct);
        var second = await ReportedWishlistTestData.AddReportAsync(
            factory,
            wishlist.Id,
            WishlistReportReason.Other,
            ct);
        await ReportedWishlistTestData.AddReportAsync(
            factory,
            secondWishlist.Id,
            WishlistReportReason.SpamOrScam,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);

        // Act
        using var groups = await ReadAsync(
            client,
            Route,
            ct);
        using var reports = await ReadAsync(
            client,
            $"{Route}/{wishlist.Id}/reports?reason=other&pageSize=1",
            ct);
        using var next = await ReadAsync(
            client,
            $"{Route}/{wishlist.Id}/reports?pageSize=1&page=2",
            ct);
        using var empty = await ReadAsync(
            client,
            $"{Route}/{wishlist.Id}/reports?reason=privacyViolation",
            ct);
        using var outside = await ReadAsync(
            client,
            $"{Route}/{wishlist.Id}/reports?page=2147483647&pageSize=100",
            ct);

        // Assert
        Assert.Equal(
            new[] {
                wishlist.Id,
                secondWishlist.Id
            }.Order(),
            groups.RootElement
                .GetProperty("items")
                .EnumerateArray()
                .Select(item => item
                    .GetProperty("wishlistId")
                    .GetGuid()));
        var ordered = new[]
        {
            first.Id,
            second.Id
        }
            .OrderDescending()
            .ToArray();
        var report = Assert.Single(reports.RootElement
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            ordered[0],
            report
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            ordered[1],
            Assert
                .Single(next.RootElement
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            "other",
            report
                .GetProperty("reason")
                .GetString());
        Assert.Equal(
            first.Details,
            report
                .GetProperty("details")
                .GetString());
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
            report
                .EnumerateObject()
                .Select(property => property.Name)
                .Order());
        Assert.Empty(empty.RootElement
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            0,
            empty.RootElement
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Empty(outside.RootElement
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            2,
            outside.RootElement
                .GetProperty("totalCount")
                .GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/reports")]
    [InlineData("/wishes/00000000-0000-0000-0000-000000000001/image")]
    public async Task GetAsync_WhenWishlistIsUnreportedOrMissing_ReturnsNotFound(string suffix)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var adminId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            ct);
        var wishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            adminId,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);

        // Act
        using var missing = await client.GetAsync(
            $"{Route}/{Guid.CreateVersion7()}{suffix}",
            ct);
        using var unreported = await client.GetAsync(
            $"{Route}/{wishlist.Id}{suffix}",
            ct);

        // Assert
        await AssertErrorAsync(
            missing,
            HttpStatusCode.NotFound,
            ct);
        await AssertErrorAsync(
            unreported,
            HttpStatusCode.NotFound,
            ct);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=invalid")]
    [InlineData("?reason=unknown")]
    [InlineData("?reason=999")]
    public async Task GetAsync_WhenPaginationOrReasonIsInvalid_ReturnsCentralizedValidation(string query)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var adminId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);

        // Act
        using var groups = await client.GetAsync(
            Route + query,
            ct);
        using var reports = await client.GetAsync(
            $"{Route}/{Guid.CreateVersion7()}/reports{query}",
            ct);

        // Assert
        await AssertErrorAsync(
            groups,
            HttpStatusCode.BadRequest,
            ct);
        await AssertErrorAsync(
            reports,
            HttpStatusCode.BadRequest,
            ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/00000000-0000-0000-0000-000000000001")]
    [InlineData("/00000000-0000-0000-0000-000000000001/reports")]
    [InlineData("/00000000-0000-0000-0000-000000000001/wishes/00000000-0000-0000-0000-000000000002/image")]
    public async Task GetAsync_WhenCallerIsNotCurrentAdministrator_RejectsEveryRoute(string suffix)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var ownerId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            ct);
        var adminId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            ct);
        using var owner = ReportedWishlistTestData.CreateClient(
            factory,
            ownerId);
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        using var anonymous = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
            var member = await manager.FindByIdAsync(adminId.ToString());
            Assert.NotNull(member);
            Assert.True((await manager.RemoveFromRoleAsync(
                    member,
                    RoleNames.Admin)).Succeeded);
        }

        // Act
        using var missing = await anonymous.GetAsync(
            Route + suffix,
            ct);
        anonymous.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            "invalid.jwt");
        using var invalid = await anonymous.GetAsync(
            Route + suffix,
            ct);
        using var deniedOwner = await owner.GetAsync(
            Route + suffix,
            ct);
        using var revoked = await admin.GetAsync(
            Route + suffix,
            ct);

        // Assert
        await AssertErrorAsync(
            missing,
            HttpStatusCode.Unauthorized,
            ct);
        await AssertErrorAsync(
            invalid,
            HttpStatusCode.Unauthorized,
            ct);
        await AssertErrorAsync(
            deniedOwner,
            HttpStatusCode.Forbidden,
            ct);
        await AssertErrorAsync(
            revoked,
            HttpStatusCode.Forbidden,
            ct);
    }

    private static async Task<JsonDocument> ReadAsync(
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
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        Assert.NotNull(document);

        return document;
    }

    private static async Task AssertErrorAsync(
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
    }
}
