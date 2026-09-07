using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using static JennGllg.Fr.MonKado.Back.Api.IntegrationTests.WishlistReportReviewTestData;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistReportReviewConcurrencyIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task GetEventsAsync_WhenReviewCommitsBetweenCountAndPage_ReturnsOneConsistentSnapshot()
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
        using var first = await UpdateAsync(
            client,
            route,
            Assert.IsType<string>(initial.Headers.ETag?.Tag),
            "upheld",
            null,
            ct);
        var interceptor = new ReportedWishlistReadInterceptor(async token =>
            {
                Assert.Equal(
                    ct,
                    token);
                using var concurrent = await UpdateAsync(
                    client,
                    route,
                    Assert.IsType<string>(first.Headers.ETag?.Tag),
                    "dismissed",
                    null,
                    token);
                Assert.Equal(
                    HttpStatusCode.OK,
                    concurrent.StatusCode);
            });
        var options = new DbContextOptionsBuilder<MonKadoDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new MonKadoDbContext(options);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = new WishlistReportReviewService(
            new WishlistReportReviewRepository(context),
            scope.ServiceProvider.GetRequiredService<IAdministratorAccessService>(),
            new WishTransactionFactory(context),
            context,
            TimeProvider.System);

        // Act
        var snapshot = await service.GetEventsAsync(
            wishlist.Id,
            report.Id,
            1,
            20,
            ct);
        using var latest = await client.GetAsync(
            route + "/events",
            ct);
        var latestBody = await latest.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Assert
        Assert.True(interceptor.Triggered);
        Assert.Equal(
            1,
            snapshot.TotalCount);
        Assert.Equal(
            JennGllg.Fr.MonKado.Back.Domain.Enums.WishlistReportStatus.Upheld,
            Assert
                .Single(snapshot.Items)
                .Status);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(
            2,
            latestBody
                .GetProperty("totalCount")
                .GetInt32());
    }

    [Fact]
    public async Task UpdateAsync_WhenAnotherReviewWinsBeforeLostAcknowledgement_DoesNotConfirmSupersededDecision()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        var interceptor = new CoordinatedAmbiguousCommitInterceptor();
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.ConfigureDbContext<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor)));
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
        interceptor.Arm();

        // Act
        var firstTask = UpdateAsync(
            client,
            route,
            Assert.IsType<string>(initial.Headers.ETag?.Tag),
            "upheld",
            null,
            ct);
        try
        {
            await interceptor.WaitForFirstCommitAsync(ct);
            using var current = await client.GetAsync(
                route,
                ct);
            using var second = await UpdateAsync(
                client,
                route,
                Assert.IsType<string>(current.Headers.ETag?.Tag),
                "dismissed",
                null,
                ct);
            Assert.Equal(
                HttpStatusCode.OK,
                second.StatusCode);
        }
        finally
        {
            interceptor.ReleaseFailure();
        }

        using var first = await firstTask;
        using var history = await client.GetAsync(
            route + "/events",
            ct);
        var events = await history.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            first.StatusCode);
        Assert.Equal(
            2,
            events
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            "dismissed",
            events.GetProperty("items")[0]
                .GetProperty("status")
                .GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAsync_WhenWishlistDeletionRacesWithReview_SerializesAndLeavesNoOrphanedHistory(bool deletionFirst)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        var barrier = new AccountDeletionCommitBarrier();
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.ConfigureDbContext<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(barrier)));
        var (adminId, ownerId, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        using var owner = ReportedWishlistTestData.CreateClient(
            factory,
            ownerId);
        var route = Route(
            wishlist.Id,
            report.Id);
        using var initial = await admin.GetAsync(
            route,
            ct);
        using var list = await owner.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}",
            ct);
        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlist.Id}");
        deleteRequest.Headers.IfMatch.Add(Assert.IsType<EntityTagHeaderValue>(list.Headers.ETag));
        var tag = Assert.IsType<string>(initial.Headers.ETag?.Tag);
        barrier.Arm();

        // Act
        var firstTask = deletionFirst ? owner.SendAsync(
            deleteRequest,
            ct) : UpdateAsync(
            admin,
            route,
            tag,
            "upheld",
            null,
            ct);
        Task<HttpResponseMessage> secondTask;
        try
        {
            await barrier.WaitUntilEnteredAsync(ct);
            secondTask = deletionFirst ? UpdateAsync(
                admin,
                route,
                tag,
                "upheld",
                null,
                ct) : owner.SendAsync(
                deleteRequest,
                ct);
        }
        finally
        {
            barrier.Release();
        }

        using var first = await firstTask;
        using var second = await secondTask;
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        // Assert
        Assert.Equal(
            deletionFirst ? HttpStatusCode.NoContent : HttpStatusCode.OK,
            first.StatusCode);
        Assert.Equal(
            deletionFirst ? HttpStatusCode.NotFound : HttpStatusCode.NoContent,
            second.StatusCode);
        Assert.Empty(await context.WishlistReportReviewEvents
                .AsNoTracking()
                .ToArrayAsync(ct));
        Assert.Empty(await context.WishlistReports
                .AsNoTracking()
                .ToArrayAsync(ct));
    }
}
