using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishImageOpenApiTests
{
    [Fact]
    public async Task GetAsync_WhenGiftImageEndpointsAreDocumented_ExposesUploadAndSignedDeliveryContracts()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI document is empty.");
        var paths = document.RootElement.GetProperty("paths");
        var privatePath = paths.GetProperty(
            "/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image");
        var upload = privatePath.GetProperty("put");
        var getOwned = privatePath.GetProperty("get");
        var getShared = paths
            .GetProperty("/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/image")
            .GetProperty("get");

        // Assert
        Assert.Equal(
            "Adds or replaces the normalized image of one owned gift wish.",
            upload.GetProperty("summary").GetString());
        AssertBearerWithoutAntiforgery(upload);
        var deletion = privatePath.GetProperty("delete");
        AssertBearerWithoutAntiforgery(deletion);
        Assert.False(deletion.TryGetProperty(
            "requestBody",
            out _));
        AssertResponses(
            deletion,
            "204",
            "400",
            "401",
            "403",
            "404",
            "412",
            "428",
            "429",
            "500",
            "503");
        AssertResponseHeaders(
            deletion.GetProperty("responses").GetProperty("204"),
            includesEntityTag: true);
        var ifMatch = Assert.Single(
            upload.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "If-Match");
        Assert.True(ifMatch.GetProperty("required").GetBoolean());
        Assert.True(
            upload.TryGetProperty(
                "requestBody",
                out var requestBody),
            upload.ToString());
        Assert.True(
            requestBody.TryGetProperty(
                "content",
                out var requestContent),
            requestBody.ToString());
        Assert.True(
            requestContent.TryGetProperty(
                "multipart/form-data",
                out var multipart),
            requestContent.ToString());
        var multipartSchema = ResolveSchema(
            document.RootElement,
            multipart.GetProperty("schema"));
        var image = multipartSchema
            .GetProperty("properties")
            .GetProperty("image");
        Assert.Equal(
            "string",
            image.GetProperty("type").GetString());
        Assert.Equal(
            "binary",
            image.GetProperty("format").GetString());
        AssertResponses(
            upload,
            "200",
            "400",
            "401",
            "403",
            "404",
            "412",
            "413",
            "415",
            "428",
            "429",
            "500",
            "503");
        AssertResponseHeaders(
            upload.GetProperty("responses").GetProperty("200"),
            includesEntityTag: true);

        Assert.Equal(
            "Gets a current private gift image through a short-lived owner grant.",
            getOwned.GetProperty("summary").GetString());
        AssertAnonymousImageOperation(getOwned);
        Assert.Equal(
            "Gets a current gift image through a short-lived active share-link grant.",
            getShared.GetProperty("summary").GetString());
        AssertAnonymousImageOperation(getShared);
    }

    private static void AssertBearerWithoutAntiforgery(JsonElement operation)
    {
        var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty(
            "Bearer",
            out _));
        Assert.DoesNotContain(
            operation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");
    }

    private static void AssertAnonymousImageOperation(JsonElement operation)
    {
        Assert.False(operation.TryGetProperty(
            "security",
            out var security) && security.GetArrayLength() > 0);
        var token = Assert.Single(
            operation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "token");
        Assert.False(token.TryGetProperty(
            "required",
            out var required) && required.GetBoolean());
        AssertResponses(
            operation,
            "200",
            "404",
            "500",
            "503");
        var success = operation.GetProperty("responses").GetProperty("200");
        Assert.True(success
            .GetProperty("content")
            .TryGetProperty(
                "image/webp",
                out _));
        AssertResponseHeaders(
            success,
            includesEntityTag: false);
    }

    private static void AssertResponses(
        JsonElement operation,
        params string[] expectedStatusCodes)
    {
        Assert.Equal(
            expectedStatusCodes.OrderBy(statusCode => statusCode),
            operation
                .GetProperty("responses")
                .EnumerateObject()
                .Select(response => response.Name)
                .OrderBy(statusCode => statusCode));
    }

    private static void AssertResponseHeaders(
        JsonElement response,
        bool includesEntityTag)
    {
        var headers = response.GetProperty("headers");
        Assert.True(headers.TryGetProperty(
            "Cache-Control",
            out _));
        Assert.Equal(
            includesEntityTag,
            headers.TryGetProperty(
                "ETag",
                out _));
    }

    private static JsonElement ResolveSchema(
        JsonElement document,
        JsonElement schema)
    {
        if (!schema.TryGetProperty(
                "$ref",
                out var reference))
            return schema;

        var name = reference.GetString()?.Split('/').Last()
            ?? throw new InvalidOperationException("The OpenAPI schema reference is invalid.");

        return document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(name);
    }
}
