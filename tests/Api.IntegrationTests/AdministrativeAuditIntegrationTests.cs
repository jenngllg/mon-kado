using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class AdministrativeAuditIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Route = "/api/v1/admin/audit-events";

    [Fact]
    public async Task GetPageAsync_WhenAllSourcesExist_ReturnsExactPrivateContractWithoutTrackingOrNewAudit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var scenario = await AdministrativeAuditTestData.CreateCompleteJournalAsync(
            factory,
            cancellationToken);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            scenario.ReaderId);

        // Act
        using var response = await client.GetAsync(
            Route,
            cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            6,
            json.GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            1,
            json.GetProperty("currentPage")
                .GetInt32());
        Assert.Equal(
            20,
            json.GetProperty("pageSize")
                .GetInt32());
        var items = json.GetProperty("items")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            6,
            items.Length);
        Assert.Equal(
            6,
            items.Select(item => item.GetProperty("action")
                .GetString())
                .Distinct()
                .Count());
        foreach (var item in items)
        {
            Assert.Equal(
                10,
                item.EnumerateObject()
                    .Count());
            Assert.Equal(
                scenario.ActorId,
                item.GetProperty("administratorId")
                    .GetGuid());
            Assert.Equal(
                "Private owner name",
                item.GetProperty("administratorDisplayName")
                    .GetString());
            Assert.Equal(
                scenario.CreatedAt,
                item.GetProperty("createdAt")
                    .GetDateTime());
            var action = item.GetProperty("action")
                .GetString();
            Assert.NotNull(action);
            var moderation = action.StartsWith(
                "wishlist",
                StringComparison.Ordinal);
            Assert.Equal(
                moderation ? JsonValueKind.String : JsonValueKind.Null,
                item.GetProperty("wishlistId")
                    .ValueKind);
            Assert.Equal(
                moderation ? JsonValueKind.Null : JsonValueKind.String,
                item.GetProperty("memberId")
                    .ValueKind);
            Assert.Equal(
                moderation ? null : "SUPPORT-806",
                item.GetProperty("requestReference")
                    .GetString());
            Assert.Equal(
                action is "wishlistSuspended" or "wishlistSuspensionReasonUpdated" ? "Private moderation reason" : null,
                item.GetProperty("reason")
                    .GetString());
            Assert.Equal(
                action.StartsWith(
                    "memberDataExport",
                    StringComparison.Ordinal) ? JsonValueKind.String : JsonValueKind.Null,
                item.GetProperty("exportId")
                    .ValueKind);
        }
        Assert.DoesNotContain(
            "@example.test",
            json.GetRawText());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1,
            await context.AdministrativeAccountErasureEvents.CountAsync(cancellationToken));
        Assert.Equal(
            2,
            await context.AdministrativeDataExportEvents.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("all", 6)]
    [InlineData("action", 1)]
    [InlineData("actor", 6)]
    [InlineData("member", 3)]
    [InlineData("wishlist", 3)]
    [InlineData("export", 2)]
    [InlineData("reference", 3)]
    [InlineData("case", 0)]
    [InlineData("from", 6)]
    [InlineData("to", 0)]
    [InlineData("range", 6)]
    [InlineData("combined", 1)]
    [InlineData("incompatible", 0)]
    [InlineData("missing", 0)]
    [InlineData("page", 6)]
    [InlineData("overflow", 6)]
    public async Task GetPageAsync_WhenFiltersAreCombined_CountsBeforeGlobalPagination(
        string kind,
        int count)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var data = await AdministrativeAuditTestData.CreateCompleteJournalAsync(
            factory,
            cancellationToken);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            data.ReaderId);
        var query = kind switch
        {
            "action" => "action=memberErased",
            "actor" => $"administratorId={data.ActorId}",
            "member" => $"memberId={data.MemberId}",
            "wishlist" => $"wishlistId={data.WishlistId}",
            "export" => $"exportId={data.ExportId}",
            "reference" => "requestReference=%20SUPPORT-806%20",
            "case" => "requestReference=support-806",
            "from" => "from=2026-09-01T14:00:00%2B02:00",
            "to" => "to=2026-09-01T12:00:00Z",
            "range" => "from=2026-09-01T12:00:00Z&to=2026-09-01T12:00:01Z",
            "combined" => $"action=memberDataExportRequested&administratorId={data.ActorId}&memberId={data.MemberId}&exportId={data.ExportId}&requestReference=SUPPORT-806",
            "incompatible" => $"memberId={data.MemberId}&wishlistId={data.WishlistId}",
            "missing" => $"memberId={Guid.CreateVersion7()}",
            "page" => "page=2&pageSize=2",
            "overflow" => "page=2147483647&pageSize=100",
            _ => ""
        };

        // Act
        using var response = await client.GetAsync(
            $"{Route}?{query}",
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResponse<AdministrativeAuditEventResponse>>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter() }
            },
            cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(
            count,
            body.TotalCount);
        var expectedItems = kind switch
        {
            "page" => 2,
            "overflow" => 0,
            _ => count
        };
        Assert.Equal(
            expectedItems,
            body.Items.Count());
        Assert.Equal(
            body.Items.OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .ThenBy(item => item.Action)
                .Select(item => item.Id),
            body.Items.Select(item => item.Id));
    }
}
