using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistShareLinkOpenApiTests
{
    [Fact]
    public async Task GetAsync_WhenShareLinkEndpointsAreDocumented_ExposesOwnerAndPublicContracts()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI document is empty.");
        var paths = document.RootElement.GetProperty("paths");
        var ownerPath = paths.GetProperty("/api/v1/wishlists/{wishlistId}/share-link");
        var create = ownerPath.GetProperty("post");
        var getOwnerLink = ownerPath.GetProperty("get");
        var rotate = ownerPath.GetProperty("put");
        var delete = ownerPath.GetProperty("delete");
        var getSharedWishlist = paths
            .GetProperty("/api/v1/shared-wishlists/{shareLinkId}")
            .GetProperty("get");

        // Assert
        Assert.Equal(
            "Creates the active share link of an owned wishlist.",
            create.GetProperty("summary").GetString());
        AssertBearerWithoutAntiforgery(create);
        AssertResponses(
            create,
            "201",
            "401",
            "403",
            "404",
            "409",
            "500",
            "503");
        AssertOwnerSuccessResponse(
            document.RootElement,
            create,
            "201",
            includesLocation: true);

        Assert.Equal(
            "Gets the active share link of an owned wishlist.",
            getOwnerLink.GetProperty("summary").GetString());
        AssertBearerWithoutAntiforgery(getOwnerLink);
        AssertResponses(
            getOwnerLink,
            "200",
            "401",
            "403",
            "404",
            "500",
            "503");
        AssertOwnerSuccessResponse(
            document.RootElement,
            getOwnerLink,
            "200",
            includesLocation: false);

        Assert.Equal(
            "Regenerates the active share-link secret.",
            rotate.GetProperty("summary").GetString());
        AssertBearerWithoutAntiforgery(rotate);
        AssertRequiredIfMatch(rotate);
        AssertResponses(
            rotate,
            "200",
            "401",
            "403",
            "404",
            "412",
            "428",
            "500",
            "503");
        AssertOwnerSuccessResponse(
            document.RootElement,
            rotate,
            "200",
            includesLocation: false);

        Assert.Equal(
            "Revokes the active share link.",
            delete.GetProperty("summary").GetString());
        AssertBearerWithoutAntiforgery(delete);
        AssertRequiredIfMatch(delete);
        AssertResponses(
            delete,
            "204",
            "401",
            "403",
            "404",
            "412",
            "428",
            "500",
            "503");
        Assert.False(delete
            .GetProperty("responses")
            .GetProperty("204")
            .TryGetProperty(
                "content",
                out _));

        Assert.Equal(
            "Gets a wishlist through its active share link.",
            getSharedWishlist.GetProperty("summary").GetString());
        Assert.False(getSharedWishlist.TryGetProperty(
            "security",
            out _));
        var shareToken = Assert.Single(
            getSharedWishlist.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "X-MonKado-Share-Token");
        Assert.Equal(
            "header",
            shareToken.GetProperty("in").GetString());
        Assert.False(shareToken.TryGetProperty(
            "required",
            out var shareTokenIsRequired) &&
            shareTokenIsRequired.GetBoolean());
        AssertResponses(
            getSharedWishlist,
            "200",
            "404",
            "429",
            "500",
            "503");
        AssertSharedSuccessResponse(
            document.RootElement,
            getSharedWishlist);
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

    private static void AssertRequiredIfMatch(JsonElement operation)
    {
        var ifMatch = Assert.Single(
            operation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "If-Match");
        Assert.True(ifMatch.GetProperty("required").GetBoolean());
    }

    private static void AssertResponses(
        JsonElement operation,
        params string[] expectedStatusCodes)
    {
        var responses = operation.GetProperty("responses");

        foreach (var statusCode in expectedStatusCodes)
        {
            Assert.True(
                responses.TryGetProperty(
                    statusCode,
                    out _),
                $"Response {statusCode} is missing.");
        }
    }

    private static void AssertOwnerSuccessResponse(
        JsonElement document,
        JsonElement operation,
        string statusCode,
        bool includesLocation)
    {
        var success = operation
            .GetProperty("responses")
            .GetProperty(statusCode);
        var headers = success.GetProperty("headers");
        Assert.True(headers.TryGetProperty(
            "Cache-Control",
            out _));
        Assert.True(headers.TryGetProperty(
            "ETag",
            out _));
        Assert.Equal(
            includesLocation,
            headers.TryGetProperty(
                "Location",
                out _));
        var schema = ResolveSchema(
            document,
            success
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "createdAt",
                "id",
                "shareUrl",
                "updatedAt"
            ],
            schema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(property => property));
    }

    private static void AssertSharedSuccessResponse(
        JsonElement document,
        JsonElement operation)
    {
        var success = operation
            .GetProperty("responses")
            .GetProperty("200");
        Assert.True(success
            .GetProperty("headers")
            .TryGetProperty(
                "Cache-Control",
                out _));
        var schema = ResolveSchema(
            document,
            success
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "eventDate",
                "id",
                "message",
                "name",
                "occasion",
                "ownerDisplayName",
                "wishes"
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
            return schema;

        var schemaName = reference.GetString()?.Split('/').Last()
            ?? throw new InvalidOperationException("The schema reference is invalid.");

        return document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName);
    }
}
