using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishOpenApiTests
{
    [Fact]
    public async Task GetAsync_WhenWishEndpointsAreDocumented_ExposesNestedPrivateResourceContracts()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(
            response.IsSuccessStatusCode,
            body + Environment.NewLine + string.Join(
                Environment.NewLine,
                factory.LogMessages));
        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");
        var collectionPath = paths.GetProperty("/api/v1/wishlists/{wishlistId}/wishes");
        var itemPath = paths.GetProperty("/api/v1/wishlists/{wishlistId}/wishes/{wishId}");
        var create = collectionPath.GetProperty("post");
        var get = itemPath.GetProperty("get");
        var update = itemPath.GetProperty("put");
        var delete = itemPath.GetProperty("delete");

        // Assert
        Assert.Equal(
            "Adds a gift wish manually to an owned private wishlist.",
            create.GetProperty("summary").GetString());
        AssertBearerWithoutAntiforgery(create);
        var requestSchema = ResolveSchema(
            document.RootElement,
            create
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "name",
                "note",
                "price",
                "url"
            ],
            requestSchema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(property => property));
        var createResponses = create.GetProperty("responses");
        AssertResponses(
            createResponses,
            "201",
            "400",
            "401",
            "403",
            "404",
            "413",
            "415",
            "500",
            "503");
        AssertSuccessHeaders(
            createResponses.GetProperty("201"),
            includesLocation: true);
        AssertWishResponseSchema(
            document.RootElement,
            createResponses.GetProperty("201"));

        Assert.Equal(
            "Gets one gift wish from an owned private wishlist.",
            get.GetProperty("summary").GetString());
        AssertBearerWithoutAntiforgery(get);
        var getResponses = get.GetProperty("responses");
        AssertResponses(
            getResponses,
            "200",
            "401",
            "403",
            "404",
            "500",
            "503");
        AssertSuccessHeaders(
            getResponses.GetProperty("200"),
            includesLocation: false);
        AssertWishResponseSchema(
            document.RootElement,
            getResponses.GetProperty("200"));

        Assert.Equal(
            "Updates a gift wish in an owned private wishlist.",
            update.GetProperty("summary").GetString());
        AssertBearerWithoutAntiforgery(update);
        var updateParameters = update.GetProperty("parameters");
        Assert.Contains(
            updateParameters.EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "If-Match" &&
                parameter.GetProperty("required").GetBoolean());
        var updateSchema = ResolveSchema(
            document.RootElement,
            update
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "name",
                "note",
                "price",
                "url"
            ],
            updateSchema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(property => property));
        var updateResponses = update.GetProperty("responses");
        AssertResponses(
            updateResponses,
            "200",
            "400",
            "401",
            "403",
            "404",
            "412",
            "413",
            "415",
            "428",
            "500",
            "503");
        AssertSuccessHeaders(
            updateResponses.GetProperty("200"),
            includesLocation: false);
        AssertWishResponseSchema(
            document.RootElement,
            updateResponses.GetProperty("200"));

        Assert.Equal(
            "Deletes a gift wish from an owned private wishlist.",
            delete.GetProperty("summary").GetString());
        AssertBearerWithoutAntiforgery(delete);
        Assert.False(delete.TryGetProperty(
            "requestBody",
            out _));
        var deleteParameters = delete.GetProperty("parameters").EnumerateArray().ToArray();
        var deleteIfMatch = Assert.Single(
            deleteParameters,
            parameter => parameter.GetProperty("name").GetString() == "If-Match");
        Assert.Equal(
            "header",
            deleteIfMatch.GetProperty("in").GetString());
        Assert.True(deleteIfMatch.GetProperty("required").GetBoolean());
        Assert.Equal(
            "Strong entity tag returned when the resource was retrieved or last modified.",
            deleteIfMatch.GetProperty("description").GetString());
        var deleteResponses = delete.GetProperty("responses");
        AssertResponses(
            deleteResponses,
            "204",
            "400",
            "401",
            "403",
            "404",
            "412",
            "428",
            "500",
            "503");
        var deleteSuccess = deleteResponses.GetProperty("204");
        Assert.False(deleteSuccess.TryGetProperty(
            "content",
            out _));
        var deleteHeaders = deleteSuccess.GetProperty("headers");
        Assert.True(deleteHeaders.TryGetProperty(
            "Cache-Control",
            out _));
        Assert.False(deleteHeaders.TryGetProperty(
            "ETag",
            out _));
        Assert.False(deleteHeaders.TryGetProperty(
            "Location",
            out _));
    }

    private static void AssertBearerWithoutAntiforgery(JsonElement operation)
    {
        var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty(
            "Bearer",
            out _));
        Assert.False(operation.TryGetProperty(
            "parameters",
            out var parameters) && parameters.EnumerateArray().Any(parameter =>
                parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN"));
    }

    private static void AssertResponses(
        JsonElement responses,
        params string[] expectedStatusCodes)
    {
        foreach (var statusCode in expectedStatusCodes)
        {
            Assert.True(
                responses.TryGetProperty(
                    statusCode,
                    out _),
                $"Response {statusCode} is missing.");
        }
    }

    private static void AssertSuccessHeaders(
        JsonElement response,
        bool includesLocation)
    {
        var headers = response.GetProperty("headers");
        Assert.Equal(
            includesLocation,
            headers.TryGetProperty(
                "Location",
                out _));
        Assert.True(headers.TryGetProperty(
            "ETag",
            out _));
        Assert.True(headers.TryGetProperty(
            "Cache-Control",
            out _));
    }

    private static void AssertWishResponseSchema(
        JsonElement document,
        JsonElement response)
    {
        var schema = ResolveSchema(
            document,
            response
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "createdAt",
                "id",
                "name",
                "note",
                "position",
                "price",
                "updatedAt",
                "url",
                "wishlistId"
            ],
            schema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(property => property));
    }

    private static JsonElement ResolveSchema(
        JsonElement document,
        JsonElement schema)
    {
        if (!schema.TryGetProperty(
            "$ref",
            out var reference))
        {
            return schema;
        }

        var schemaName = reference.GetString()?.Split('/').Last()
            ?? throw new InvalidOperationException("The schema reference is invalid.");

        return document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName);
    }
}
