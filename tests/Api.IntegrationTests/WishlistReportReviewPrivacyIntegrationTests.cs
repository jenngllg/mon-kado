using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using static JennGllg.Fr.MonKado.Back.Api.IntegrationTests.WishlistReportReviewTestData;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistReportReviewPrivacyIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task UpdateAsync_WhenPrivateNoteIsOmitted_ClearsCurrentNoteButKeepsHistoryWithoutLoggingContent()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        using var logs = new CapturingGoogleLoggerProvider();
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.AddLogging(logging => logging.AddProvider(logs)));
        var (adminId, _, wishlist, report) = await PrepareAsync(
            factory,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        var route = Route(
            wishlist.Id,
            report.Id);
        const string privateNote = "Confidential moderator reasoning for the report";
        using var initial = await client.GetAsync(
            route,
            ct);
        using var first = await UpdateAsync(
            client,
            route,
            Assert.IsType<string>(initial.Headers.ETag?.Tag),
            "upheld",
            privateNote,
            ct);
        Assert.Equal(
            HttpStatusCode.OK,
            first.StatusCode);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            route);
        request.Headers.IfMatch.Add(Assert.IsType<System.Net.Http.Headers.EntityTagHeaderValue>(first.Headers.ETag));
        request.Content = JsonContent.Create(new
        {
            status = "upheld"
        });

        // Act
        using var response = await client.SendAsync(
            request,
            ct);
        var current = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var historyResponse = await client.GetAsync(
            route + "/events",
            ct);
        var history = await historyResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            JsonValueKind.Null,
            current
                .GetProperty("reviewNote")
                .ValueKind);
        Assert.NotEqual(
            first.Headers.ETag,
            response.Headers.ETag);
        Assert.Equal(
            2,
            history
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            JsonValueKind.Null,
            history.GetProperty("items")[0]
                .GetProperty("note")
                .ValueKind);
        Assert.Equal(
            privateNote,
            history.GetProperty("items")[1]
                .GetProperty("note")
                .GetString());
        Assert.All(
            logs.Entries,
            entry =>
            {
                Assert.DoesNotContain(
                    privateNote,
                    entry.Value,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    Assert.IsType<string>(report.Details),
                    entry.Value,
                    StringComparison.Ordinal);
            });
        Assert.Contains(
            logs.Entries,
            entry => entry.Key == LogEventIds.WishlistReportReviewed);
        Assert.Contains(
            logs.Entries,
            entry => entry.Key == LogEventIds.WishlistReportRetrieved);
        Assert.Contains(
            logs.Entries,
            entry => entry.Key == LogEventIds.WishlistReportReviewEventsRetrieved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/reports")]
    public async Task GetPageAsync_WhenStatusFilterIsUnknown_ReturnsBadRequest(string suffix)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var (adminId, _, wishlist, _) = await PrepareAsync(
            factory,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        var route = "/api/v1/admin/reported-wishlists";

        if (suffix.Length != 0)
            route += $"/{wishlist.Id}{suffix}";

        // Act
        using var response = await client.GetAsync(
            route + "?status=unknown",
            ct);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }
}
