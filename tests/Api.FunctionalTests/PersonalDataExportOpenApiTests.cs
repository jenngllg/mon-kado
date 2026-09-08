using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class PersonalDataExportOpenApiTests
{
    private static readonly string[] _metadataProperties = [
        "createdAt",
        "errorCode",
        "expiresAt",
        "id",
        "readyAt",
        "sizeInBytes",
        "snapshotAt",
        "status"
    ];
    [Theory]
    [InlineData("/api/v1/members/current/data-exports", false)]
    [InlineData("/api/v1/admin/members/{memberId}/data-exports", true)]
    public async Task GetAsync_WhenExportsAreDocumented_ExposesBearerBinaryArchiveAndExactMetadata(
        string route,
        bool administrative)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        var document = await client.GetFromJsonAsync<JsonElement>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        var paths = document.GetProperty("paths");
        var post = paths
            .GetProperty(route)
            .GetProperty("post");
        var latest = paths
            .GetProperty($"{route}/latest")
            .GetProperty("get");
        var details = paths
            .GetProperty($"{route}/{{exportId}}")
            .GetProperty("get");
        var archive = paths
            .GetProperty($"{route}/{{exportId}}/archive")
            .GetProperty("get");

        // Assert
        var operations = new[]
        {
            post,
            latest,
            details,
            archive
        };
        foreach (var operation in operations)
        {
            Assert.True(Assert
                    .Single(operation
                        .GetProperty("security")
                        .EnumerateArray())
                    .TryGetProperty(
                    "Bearer",
                    out _));
            Assert.Equal(
                administrative && operation.Equals(post),
                operation.TryGetProperty(
                    "requestBody",
                    out _));
            Assert.True(operation
                    .GetProperty("responses")
                    .TryGetProperty(
                    "401",
                    out _));
            Assert.True(operation
                    .GetProperty("responses")
                    .TryGetProperty(
                    "429",
                    out _));
            Assert.True(operation
                    .GetProperty("responses")
                    .TryGetProperty(
                    "503",
                    out _));
            Assert.True(operation
                    .GetProperty("responses")
                    .TryGetProperty(
                    "500",
                    out _));

            if (operation.TryGetProperty(
                "parameters",
                out var parameters))
                Assert.DoesNotContain(
                    parameters.EnumerateArray(),
                    parameter => parameter
                        .GetProperty("name")
                        .GetString() is "If-Match" or "X-CSRF-TOKEN" || parameter
                        .GetProperty("in")
                        .GetString() == "cookie");
        }

        if (administrative)
        {
            var request = document
                .GetProperty("components")
                .GetProperty("schemas")
                .GetProperty("RequestAdministrativeDataExportRequest")
                .GetProperty("properties");
            Assert.Equal(
                "requestReference",
                Assert.Single(request.EnumerateObject()).Name);
            Assert.All(
                operations,
                operation => Assert.True(operation
                    .GetProperty("responses")
                    .TryGetProperty(
                        "403",
                        out _)));
        }

        Assert.True(post
                .GetProperty("responses")
                .GetProperty("202")
                .GetProperty("headers")
                .TryGetProperty(
                "Location",
                out _));
        Assert.True(post
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("headers")
                .TryGetProperty(
                "Location",
                out _));
        var zipResponse = archive
            .GetProperty("responses")
            .GetProperty("200");
        Assert.True(zipResponse
                .GetProperty("headers")
                .TryGetProperty(
                "Content-Disposition",
                out _));
        Assert.True(zipResponse
                .GetProperty("headers")
                .TryGetProperty(
                "X-Content-Type-Options",
                out _));
        Assert.True(zipResponse
                .GetProperty("headers")
                .TryGetProperty(
                "Cache-Control",
                out _));
        var schema = zipResponse
            .GetProperty("content")
            .GetProperty("application/zip")
            .GetProperty("schema");
        Assert.Equal(
            "string",
            schema
                .GetProperty("type")
                .GetString());
        Assert.Equal(
            "binary",
            schema
                .GetProperty("format")
                .GetString());
        Assert.True(archive
                .GetProperty("responses")
                .TryGetProperty(
                "409",
                out _));
        Assert.True(archive
                .GetProperty("responses")
                .TryGetProperty(
                "404",
                out _));
        var metadata = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("PersonalDataExportResponse");
        Assert.Equal(
            _metadataProperties,
            metadata
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenFrontendOriginIsAllowed_ExposesDownloadAndStatusHeadersWithoutCookies()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/members/current/data-exports/latest");
        request.Headers.Add(
            "Origin",
            "http://localhost:5173");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var exposed = string.Join(
            ",",
            response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains(
            "Location",
            exposed,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Content-Disposition",
            exposed,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Retry-After",
            exposed,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }
}
