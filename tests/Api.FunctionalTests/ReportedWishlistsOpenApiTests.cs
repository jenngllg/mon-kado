using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class ReportedWishlistsOpenApiTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("/{wishlistId}", false)]
    [InlineData("/{wishlistId}/reports", false)]
    [InlineData("/{wishlistId}/wishes/{wishId}/image", true)]
    public async Task GetAsync_WhenReportsAreDocumented_RequiresBearerAndExposesReadOnlyContracts(
        string suffix,
        bool image)
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
        var path = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/admin/reported-wishlists" + suffix);
        Assert.Equal(
            ["get"],
            path
                .EnumerateObject()
                .Select(property => property.Name));
        var operation = path.GetProperty("get");
        Assert.True(Assert
                .Single(operation
                    .GetProperty("security")
                    .EnumerateArray())
                .TryGetProperty(
                "Bearer",
                out _));
        Assert.False(operation.TryGetProperty(
                "requestBody",
                out _));
        Assert.False(string.IsNullOrWhiteSpace(operation
                    .GetProperty("summary")
                    .GetString()));
        var responses = operation.GetProperty("responses");
        string[] expectedStatuses = [
            "200",
            "400",
            "401",
            "403",
            "429",
            "500",
            "503"
        ];
        Assert.All(
            expectedStatuses,
            status => Assert.True(responses.TryGetProperty(
                    status,
                    out _)));

        if (suffix.Length > 0)
            Assert.True(responses.TryGetProperty(
                    "404",
                    out _));
        var success = responses.GetProperty("200");
        Assert.True(success
                .GetProperty("content")
                .TryGetProperty(
                image ? "image/webp" : "application/json",
                out _));

        if (image)
        {
            var schemaReference = success
                .GetProperty("content")
                .GetProperty("image/webp")
                .GetProperty("schema");
            Assert.Equal(
                "#/components/schemas/Stream",
                schemaReference
                    .GetProperty("$ref")
                    .GetString());
            var schema = document.RootElement
                .GetProperty("components")
                .GetProperty("schemas")
                .GetProperty("Stream");
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
        }
        Assert.True(success
                .GetProperty("headers")
                .TryGetProperty(
                "Cache-Control",
                out _));

        if (operation.TryGetProperty(
            "parameters",
            out var parameters))
            Assert.DoesNotContain(
                parameters.EnumerateArray(),
                parameter => parameter
                    .GetProperty("name")
                    .GetString() is "token" or "If-Match" or "X-CSRF-TOKEN");
    }
}
