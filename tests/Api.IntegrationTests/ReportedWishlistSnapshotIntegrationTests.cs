using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class ReportedWishlistSnapshotIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPageAsync_WhenReportCommitsBetweenCountAndPage_KeepsOneRepeatableReadSnapshot(bool reportsPage)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var ownerId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            ct);
        var wishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            ct);
        var other = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            ct);
        var original = await ReportedWishlistTestData.AddReportAsync(
            factory,
            wishlist.Id,
            WishlistReportReason.Other,
            ct);
        var interceptor = new ReportedWishlistReadInterceptor(async token =>
            {
                Assert.Equal(
                    ct,
                    token);
                await ReportedWishlistTestData.AddReportAsync(
                    factory,
                    reportsPage ? wishlist.Id : other.Id,
                    WishlistReportReason.Other,
                    token);
            });
        var options = new DbContextOptionsBuilder<MonKadoDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new MonKadoDbContext(options);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = new ReportedWishlistService(
            context,
            new WishTransactionFactory(context),
            scope.ServiceProvider.GetRequiredService<IGiftImageStore>());

        // Act
        var page = reportsPage ? null : await service.GetPageAsync(
            null,
            null,
            1,
            20,
            ct);
        var reports = reportsPage ? await service.GetReportsAsync(
            wishlist.Id,
            null,
            1,
            20,
            ct) : null;

        // Assert
        Assert.True(interceptor.Triggered);
        Assert.Empty(context.ChangeTracker.Entries());

        if (reportsPage)
        {
            Assert.NotNull(reports);
            Assert.Equal(
                1,
                reports.TotalCount);
            Assert.Equal(
                original.Id,
                Assert
                    .Single(reports.Items)
                    .Id);

            return;
        }

        Assert.NotNull(page);
        Assert.Equal(
            1,
            page.TotalCount);
        Assert.Equal(
            wishlist.Id,
            Assert
                .Single(page.Items)
                .WishlistId);
    }
}
