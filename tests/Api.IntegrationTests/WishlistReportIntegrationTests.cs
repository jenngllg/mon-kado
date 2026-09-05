using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistReportIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime _now = new(
        2026,
        9,
        5,
        10,
        0,
        0,
        DateTimeKind.Utc);

    [Fact]
    public async Task ReportAsync_WhenShareLinkIsValid_PersistsEveryAnonymousReport()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var sharedWishlist = await SeedSharedWishlistAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();

        // Act
        using var firstResponse = await ReportAsync(
            client,
            sharedWishlist.ShareLinkId,
            sharedWishlist.Secret,
            "spamOrScam",
            null,
            cancellationToken);
        using var secondResponse = await ReportAsync(
            client,
            sharedWishlist.ShareLinkId,
            sharedWishlist.Secret,
            "other",
            "  De\u0301tails  ",
            cancellationToken);
        using var duplicateResponse = await ReportAsync(
            client,
            sharedWishlist.ShareLinkId,
            sharedWishlist.Secret,
            "spamOrScam",
            null,
            cancellationToken);
        var reports = await GetReportsAsync(
            factory,
            cancellationToken);
        var columns = await GetReportColumnsAsync(
            factory,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            secondResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            duplicateResponse.StatusCode);
        var spamReports = reports
            .Where(report => report.Reason is WishlistReportReason.SpamOrScam)
            .ToArray();
        Assert.Equal(
            2,
            spamReports.Length);
        Assert.All(
            spamReports,
            report => Assert.Null(report.Details));
        var otherReport = Assert.Single(
            reports,
            report => report.Reason is WishlistReportReason.Other);
        Assert.Equal(
            "Détails",
            otherReport.Details);
        Assert.Equal(
            reports.Count,
            reports.Select(report => report.Id)
                .Distinct()
                .Count());
        Assert.All(
            reports,
            report =>
            {
                Assert.Equal(
                    sharedWishlist.WishlistId,
                    report.WishlistId);
                Assert.Equal(
                    7,
                    report.Id.Version);
                Assert.Equal(
                    _now,
                    report.CreatedAt);
                Assert.Null(report.UpdatedAt);
            });
        Assert.Equal(
            [
                "created_at",
                "details",
                "id",
                "reason",
                "updated_at",
                "wishlist_id"
            ],
            columns);
    }

    [Fact]
    public async Task ReportAsync_WhenSecretIsInvalid_ReturnsNotFoundWithoutPersistingReport()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var sharedWishlist = await SeedSharedWishlistAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();

        // Act
        using var response = await ReportAsync(
            client,
            sharedWishlist.ShareLinkId,
            "invalid",
            "spamOrScam",
            null,
            cancellationToken);
        var reports = await GetReportsAsync(
            factory,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task ReportAsync_WhenShareLinkWasRevoked_ReturnsNotFoundWithoutPersistingReport()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var sharedWishlist = await SeedSharedWishlistAsync(
            factory,
            cancellationToken);
        await DeleteShareLinkAsync(
            factory,
            sharedWishlist.ShareLinkId,
            cancellationToken);
        using var client = factory.CreateClient();

        // Act
        using var response = await ReportAsync(
            client,
            sharedWishlist.ShareLinkId,
            sharedWishlist.Secret,
            "spamOrScam",
            null,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        Assert.Empty(await GetReportsAsync(
            factory,
            cancellationToken));
    }

    [Fact]
    public async Task DeleteWishlistAsync_WhenReportExists_DeletesReportByCascade()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var sharedWishlist = await SeedSharedWishlistAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();
        using var reportResponse = await ReportAsync(
            client,
            sharedWishlist.ShareLinkId,
            sharedWishlist.Secret,
            "privacyViolation",
            null,
            cancellationToken);

        // Act
        await DeleteWishlistAsync(
            factory,
            sharedWishlist.WishlistId,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            reportResponse.StatusCode);
        Assert.Empty(await GetReportsAsync(
            factory,
            cancellationToken));
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(CancellationToken cancellationToken)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            new FixedTimeProvider(_now));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await fixture.ResetDatabaseAsync(cancellationToken);

        return factory;
    }

    private static async Task<(
        Guid WishlistId,
        Guid ShareLinkId,
        string Secret)> SeedSharedWishlistAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        var token = tokenService.Create();
        context.Users.Add(new MonKadoUser
        {
            Id = ownerId,
            UserName = "owner@example.test",
            NormalizedUserName = "OWNER@EXAMPLE.TEST",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
            EmailConfirmed = true,
            DisplayName = "Jenn",
            SecurityStamp = Guid.CreateVersion7().ToString()
        });
        context.Wishlists.Add(new Wishlist(
            wishlistId,
            ownerId,
            "Birthday",
            "BIRTHDAY",
            WishlistOccasion.Birthday,
            null,
            null));
        context.WishlistShareLinks.Add(new WishlistShareLink(
            shareLinkId,
            wishlistId,
            token.SecretHash,
            token.ProtectedSecret));
        await context.SaveChangesAsync(cancellationToken);

        return (
            wishlistId,
            shareLinkId,
            token.Secret);
    }

    private static async Task<HttpResponseMessage> ReportAsync(
        HttpClient client,
        Guid shareLinkId,
        string secret,
        string reason,
        string? details,
        CancellationToken cancellationToken)
    {
        var csrfToken = await GetCsrfTokenAsync(
            client,
            cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/shared-wishlists/{shareLinkId}/reports")
        {
            Content = JsonContent.Create(new
            {
                reason,
                details
            })
        };
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            secret);
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var response = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/security/csrf-token",
            cancellationToken);

        return response?.Token
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
    }

    private static async Task<IReadOnlyCollection<WishlistReport>> GetReportsAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.WishlistReports
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<IReadOnlyCollection<string>> GetReportColumnsAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'wishlist_reports'
            ORDER BY column_name;
            """;
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task DeleteShareLinkAsync(
        PostgreSqlApiFactory factory,
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.WishlistShareLinks
            .Where(shareLink => shareLink.Id == shareLinkId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task DeleteWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Wishlists
            .Where(wishlist => wishlist.Id == wishlistId)
            .ExecuteDeleteAsync(cancellationToken);
    }

}
