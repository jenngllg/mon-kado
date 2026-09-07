using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistModerationOpenApiTests
{
    [Theory]
    [InlineData("/api/v1/wishlists/{wishlistId}", "put")]
    [InlineData("/api/v1/wishlists/{wishlistId}", "delete")]
    [InlineData("/api/v1/wishlists/{wishlistId}/wishes", "post")]
    [InlineData("/api/v1/wishlists/{wishlistId}/wishes", "patch")]
    [InlineData("/api/v1/wishlists/{wishlistId}/wishes/{wishId}", "put")]
    [InlineData("/api/v1/wishlists/{wishlistId}/wishes/{wishId}", "delete")]
    [InlineData("/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image", "put")]
    [InlineData("/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image", "delete")]
    [InlineData("/api/v1/wishlists/{wishlistId}/share-link", "get")]
    [InlineData("/api/v1/wishlists/{wishlistId}/share-link", "post")]
    [InlineData("/api/v1/wishlists/{wishlistId}/share-link", "put")]
    [InlineData("/api/v1/wishlists/{wishlistId}/share-link", "delete")]
    [InlineData("/api/v1/wishlists/{wishlistId}/wish-import-previews", "post")]
    public async Task GetAsync_WhenOwnerOperationCanBeSuspended_DocumentsStructuredConflict(
        string path,
        string method)
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
            .GetProperty(path)
            .GetProperty(method);
        var conflict = operation
            .GetProperty("responses")
            .GetProperty("409");
        var schema = ResolveSchema(
            document.RootElement,
            conflict
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "errorCode",
                "message",
                "statusCode",
                "title",
                "validationErrors"
            ],
            schema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name));
    }

    [Fact]
    public async Task GetAsync_WhenModerationIsDocumented_SeparatesAdministratorDecisionsFromOwnerUpdates()
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
        var paths = document.RootElement.GetProperty("paths");
        var moderation = paths.GetProperty("/api/v1/admin/wishlists/{wishlistId}/moderation");
        var update = moderation.GetProperty("put");
        var request = ResolveSchema(
            document.RootElement,
            update
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "isSuspended",
                "reason"
            ],
            request
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name));
        var ifMatch = Assert.Single(
            update
                .GetProperty("parameters")
                .EnumerateArray(),
            parameter => parameter
                .GetProperty("name")
                .GetString() == "If-Match");
        Assert.True(ifMatch
                .GetProperty("required")
                .GetBoolean());
        var events = paths
            .GetProperty("/api/v1/admin/wishlists/{wishlistId}/moderation/events")
            .GetProperty("get");
        var operations = new[]
        {
            moderation.GetProperty("get"),
            update,
            events
        };
        Assert.All(
            operations,
            operation =>
            {
                var security = Assert.Single(operation
                        .GetProperty("security")
                        .EnumerateArray());
                Assert.True(security.TryGetProperty(
                        "Bearer",
                        out _));
                Assert.True(operation
                        .GetProperty("responses")
                        .TryGetProperty(
                        "401",
                        out _));
                Assert.True(operation
                        .GetProperty("responses")
                        .TryGetProperty(
                        "403",
                        out _));
                Assert.True(operation
                        .GetProperty("responses")
                        .GetProperty("200")
                        .GetProperty("headers")
                        .TryGetProperty(
                        "Cache-Control",
                        out _));
            });
        var ownerUpdate = paths
            .GetProperty("/api/v1/wishlists/{wishlistId}")
            .GetProperty("put");
        var ownerRequest = ResolveSchema(
            document.RootElement,
            ownerUpdate
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "eventDate",
                "message",
                "name",
                "occasion"
            ],
            ownerRequest
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name));
    }

    private static JsonElement ResolveSchema(
        JsonElement document,
        JsonElement schema)
    {

        if (!schema.TryGetProperty(
            "$ref",
            out var reference))
            return schema;
        var path = Assert.IsType<string>(reference.GetString());
        var name = path
            .Split('/')
            .Last();

        return document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(name);
    }
}
