using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishImportPreviewOpenApiTests
{
    [Fact]
    public async Task GetAsync_WhenImportIsDocumented_ExposesPreviewAndConfirmationContract()
    {
        // Arrange
        await using var factory = new WishImportApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var operation = json.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/wishlists/{wishlistId}/wish-import-previews")
            .GetProperty("post");

        // Assert
        Assert.Equal(
            "Analyzes a public URL before the owner confirms gift creation.",
            operation
                .GetProperty("summary")
                .GetString());
        Assert.Contains(
            "retry only the upload",
            operation
                .GetProperty("description")
                .GetString(),
            StringComparison.Ordinal);
        Assert.True(operation.TryGetProperty(
                "security",
                out _));
        var responses = operation.GetProperty("responses");
        Assert.Equal(
            [
                "application/json",
                "application/*+json"
            ],
            operation
                .GetProperty("requestBody")
                .GetProperty("content")
                .EnumerateObject()
                .Select(property => property.Name));
        foreach (var status in new[]
        {
            "200",
            "400",
            "401",
            "404",
            "413",
            "429",
            "503",
            "500"
        }

        )
            Assert.True(responses.TryGetProperty(
                    status,
                    out _));
        var schema = json.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("WishImportPreview");
        Assert.Equal(
            [
                "name",
                "url",
                "price",
                "quantity",
                "image",
                "warnings"
            ],
            schema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.False(operation
                .GetRawText()
                .Contains(
                "X-CSRF-TOKEN",
                StringComparison.OrdinalIgnoreCase));
    }
}
