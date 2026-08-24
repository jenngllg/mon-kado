using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistOpenApiTests
{
    [Fact]
    public async Task GetAsync_WhenWishlistEndpointsAreDocumented_ExposesPrivateResourceContracts()
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
        var create = paths
            .GetProperty("/api/v1/wishlists")
            .GetProperty("post");
        var get = paths
            .GetProperty("/api/v1/wishlists/{wishlistId}")
            .GetProperty("get");

        // Assert
        Assert.Equal(
            "Creates a private wishlist for the current member.",
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
                "eventDate",
                "message",
                "name",
                "occasion"
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
            "409",
            "413",
            "415",
            "500",
            "503");
        var createHeaders = createResponses
            .GetProperty("201")
            .GetProperty("headers");
        Assert.True(createHeaders.TryGetProperty(
            "Location",
            out _));
        Assert.True(createHeaders.TryGetProperty(
            "ETag",
            out _));
        Assert.True(createHeaders.TryGetProperty(
            "Cache-Control",
            out _));
        AssertWishlistResponseSchema(
            document.RootElement,
            createResponses.GetProperty("201"));

        Assert.Equal(
            "Gets a private wishlist owned by the current member.",
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
        var getHeaders = getResponses
            .GetProperty("200")
            .GetProperty("headers");
        Assert.True(getHeaders.TryGetProperty(
            "ETag",
            out _));
        Assert.True(getHeaders.TryGetProperty(
            "Cache-Control",
            out _));
        AssertWishlistResponseSchema(
            document.RootElement,
            getResponses.GetProperty("200"));
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

    private static void AssertWishlistResponseSchema(
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
                "eventDate",
                "id",
                "message",
                "name",
                "occasion",
                "updatedAt"
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
