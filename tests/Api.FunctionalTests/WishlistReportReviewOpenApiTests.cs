using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistReportReviewOpenApiTests
{
    [Theory]
    [InlineData("get", "", true)]
    [InlineData("put", "", true)]
    [InlineData("get", "/events", false)]
    public async Task GetAsync_WhenReviewContractsArePublished_DocumentsBearerPreconditionsAndPrivateState(
        string method,
        string suffix,
        bool versioned)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(document);
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/admin/reported-wishlists/{wishlistId}/reports/{reportId}" + suffix)
            .GetProperty(method);
        Assert.True(Assert
                .Single(operation
                    .GetProperty("security")
                    .EnumerateArray())
                .TryGetProperty(
                "Bearer",
                out _));
        Assert.False(string.IsNullOrWhiteSpace(operation
                    .GetProperty("summary")
                    .GetString()));
        var responses = operation.GetProperty("responses");
        Assert.All<string>(
            [
                "200",
                "400",
                "401",
                "403",
                "404",
                "500",
                "503"
            ],
            status => Assert.True(responses.TryGetProperty(
                    status,
                    out _)));
        var headers = responses
            .GetProperty("200")
            .GetProperty("headers");
        Assert.True(headers.TryGetProperty(
                "Cache-Control",
                out _));
        Assert.Equal(
            versioned,
            headers.TryGetProperty(
                "ETag",
                out _));
        var parameters = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .ToArray();
        Assert.DoesNotContain(
            parameters,
            parameter => parameter
                .GetProperty("name")
                .GetString() is "token" or "X-CSRF-TOKEN");

        if (method == "put")
        {
            Assert.All<string>(
                [
                    "412",
                    "413",
                    "415",
                    "428"
                ],
                status => Assert.True(responses.TryGetProperty(
                        status,
                        out _)));
            var tag = Assert.Single(
                parameters,
                parameter => parameter
                    .GetProperty("name")
                    .GetString() == "If-Match");
            Assert.True(tag
                    .GetProperty("required")
                    .GetBoolean());
            Assert.True(operation
                    .GetProperty("requestBody")
                    .GetProperty("content")
                    .TryGetProperty(
                    "application/json",
                    out _));
        }

        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        var report = schemas
            .GetProperty("WishlistReportDetails")
            .GetProperty("properties");
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
        Assert.Equal(
            [
                "pending",
                "upheld",
                "dismissed"
            ],
            schemas
                .GetProperty("WishlistReportStatus")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            [
                "reviewNote",
                "status"
            ],
            schemas
                .GetProperty("UpdateWishlistReportReviewRequest")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .Order());
        Assert.Equal(
            [
                "all",
                "dismissed",
                "pending",
                "upheld"
            ],
            schemas
                .GetProperty("WishlistReportStatusFilter")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Order());
    }
}
