using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class AdministrativeAuditLifecycleIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Route = "/api/v1/admin/audit-events";

    [Fact]
    public async Task GetPageAsync_WhenSourcesShareDateAndIdentifier_UsesActionAsFinalTieBreaker()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var data = await AdministrativeAuditTestData.CreateCompleteJournalAsync(
            factory,
            ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var id = await context.AdministrativeAccountErasureEvents
            .Select(entry => entry.Id)
            .SingleAsync(ct);
        await context.WishlistModerationEvents
            .Where(entry => entry.Action == JennGllg.Fr.MonKado.Back.Domain.Enums.WishlistModerationAction.Suspended)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    entry => entry.Id,
                    id),
                ct);
        await context.AdministrativeDataExportEvents
            .Where(entry => entry.Action == AdministrativeDataExportAction.Requested)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    entry => entry.Id,
                    id),
                ct);
        var service = scope.ServiceProvider.GetRequiredService<IAdministrativeAuditService>();

        // Act
        var page = await service.GetPageAsync(
            new AdministrativeAuditFilter
            {
                Page = 1,
                PageSize = 20
            },
            ct);

        // Assert
        Assert.Equal(
            [
                AdministrativeAuditAction.WishlistSuspended,
                AdministrativeAuditAction.MemberDataExportRequested,
                AdministrativeAuditAction.MemberErased
            ],
            page.Items
                .Where(entry => entry.Id == id)
                .Select(entry => entry.Action));
        Assert.Equal(
            data.CreatedAt,
            page.Items
                .First().CreatedAt);
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("revoke")]
    [InlineData("delete")]
    public async Task GetPageAsync_WhenActorChanges_PreservesEventsWithCurrentIdentity(string change)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var data = await AdministrativeAuditTestData.CreateCompleteJournalAsync(
            factory,
            ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        switch (change)
        {
            case "rename":
                await context.Users
                    .Where(user => user.Id == data.ActorId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            user => user.DisplayName,
                            "New public name"),
                        ct);
                break;
            case "revoke":
                await context.UserRoles
                    .Where(role => role.UserId == data.ActorId)
                    .ExecuteDeleteAsync(ct);
                break;
            case "delete":
                await context.Users
                    .Where(user => user.Id == data.ActorId)
                    .ExecuteDeleteAsync(ct);
                break;
        }
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            data.ReaderId,
            TestContext.Current.CancellationToken);

        // Act
        var body = await client.GetFromJsonAsync<JsonElement>(
            Route,
            ct);

        // Assert
        Assert.Equal(
            6,
            body.GetProperty("totalCount")
                .GetInt32());
        foreach (var item in body.GetProperty("items")
            .EnumerateArray())
        {

            if (change == "delete")
            {
                Assert.Equal(
                    JsonValueKind.Null,
                    item.GetProperty("administratorId")
                        .ValueKind);
                Assert.Equal(
                    JsonValueKind.Null,
                    item.GetProperty("administratorDisplayName")
                        .ValueKind);
                continue;
            }
            Assert.Equal(
                data.ActorId,
                item.GetProperty("administratorId")
                    .GetGuid());
            Assert.Equal(
                change == "rename" ? "New public name" : "Private owner name",
                item.GetProperty("administratorDisplayName")
                    .GetString());
        }
    }

    [Fact]
    public async Task GetPageAsync_WhenTargetsDisappear_PreservesExistingRetentionAndNullReferences()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var data = await AdministrativeAuditTestData.CreateCompleteJournalAsync(
            factory,
            ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Wishlists
            .Where(wishlist => wishlist.Id == data.WishlistId)
            .ExecuteDeleteAsync(ct);
        await context.Users
            .Where(user => user.Id == data.MemberId)
            .ExecuteDeleteAsync(ct);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            data.ReaderId,
            TestContext.Current.CancellationToken);

        // Act
        var body = await client.GetFromJsonAsync<JsonElement>(
            Route,
            ct);

        // Assert
        Assert.Equal(
            3,
            body.GetProperty("totalCount")
                .GetInt32());
        foreach (var item in body.GetProperty("items")
            .EnumerateArray())
        {
            Assert.Equal(
                JsonValueKind.Null,
                item.GetProperty("wishlistId")
                    .ValueKind);

            if (item.GetProperty("action")
                .GetString() == "memberErased")
                Assert.Equal(
                    data.MemberId,
                    item.GetProperty("memberId")
                        .GetGuid());
            else
                Assert.Equal(
                    JsonValueKind.Null,
                    item.GetProperty("memberId")
                        .ValueKind);
        }
    }

    [Theory]
    [InlineData("member", HttpStatusCode.Forbidden)]
    [InlineData("revoked", HttpStatusCode.Forbidden)]
    [InlineData("missing", HttpStatusCode.Unauthorized)]
    [InlineData("invalid", HttpStatusCode.Unauthorized)]
    [InlineData("cookie", HttpStatusCode.Unauthorized)]
    public async Task GetPageAsync_WhenCallerCannotRead_DeniesUsingCurrentDatabaseAuthorization(
        string authentication,
        HttpStatusCode expected)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var data = await AdministrativeAuditTestData.CreateCompleteJournalAsync(
            factory,
            ct);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            authentication == "member" ? data.MemberId : data.ReaderId,
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        switch (authentication)
        {
            case "revoked":
                await context.UserRoles
                    .Where(role => role.UserId == data.ReaderId)
                    .ExecuteDeleteAsync(ct);
                break;
            case "missing":
                client.DefaultRequestHeaders.Authorization = null;
                break;
            case "invalid":
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    "invalid.jwt.signature");
                break;
            case "cookie":
                var session = await scope.ServiceProvider.GetRequiredService<IRefreshSessionService>()
                    .CreateAsync(
                        data.ReaderId,
                        false,
                        null,
                        null,
                        ct);
                await context.SaveChangesAsync(ct);
                client.DefaultRequestHeaders.Authorization = null;
                client.DefaultRequestHeaders.Add(
                    "Cookie",
                    $"MonKado.Refresh={session.RefreshToken}");
                break;
        }

        // Act
        using var response = await client.GetAsync(
            Route,
            ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Assert
        Assert.Equal(
            expected,
            response.StatusCode);
        Assert.Equal(
            (int)expected,
            body.GetProperty("statusCode")
                .GetInt32());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPageAsync_WhenInsertionOrPurgeCommitsAfterCount_UsesOneUntrackedSnapshot(bool purge)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var data = await AdministrativeAuditTestData.CreateCompleteJournalAsync(
            factory,
            ct);
        var interceptor = new ReportedWishlistReadInterceptor(async token =>
        {
            Assert.Equal(
                ct,
                token);
            await using var scope = factory.Services.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

            if (purge)
            {
                await writer.AdministrativeDataExportEvents.ExecuteDeleteAsync(token);

                return;
            }
            writer.AdministrativeDataExportEvents.Add(new AdministrativeDataExportEvent(
                data.ActorId,
                data.MemberId,
                Guid.CreateVersion7(),
                AdministrativeDataExportAction.Requested,
                "CONCURRENT",
                data.CreatedAt.AddMinutes(1)));
            await writer.SaveChangesAsync(token);
        });
        var options = new DbContextOptionsBuilder<MonKadoDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new MonKadoDbContext(options);
        var service = new AdministrativeAuditService(
            context,
            new WishTransactionFactory(context));
        var filter = new AdministrativeAuditFilter
        {
            Page = 1,
            PageSize = 20
        };

        // Act
        var snapshot = await service.GetPageAsync(
            filter,
            ct);
        var next = await service.GetPageAsync(
            filter,
            ct);

        // Assert
        Assert.True(interceptor.Triggered);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(
            6,
            snapshot.TotalCount);
        Assert.Equal(
            6,
            snapshot.Items.Count());
        Assert.Equal(
            purge ? 4 : 7,
            next.TotalCount);
        Assert.Equal(
            next.TotalCount,
            next.Items.Count());
        Assert.DoesNotContain(
            snapshot.Items,
            item => item.RequestReference == "CONCURRENT");
    }
}
