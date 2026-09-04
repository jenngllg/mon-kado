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
        var getSharedWish = paths
            .GetProperty("/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}")
            .GetProperty("get");
        var participantsPath = paths
            .GetProperty("/api/v1/shared-wishlists/{shareLinkId}/participants");
        var joinSharedWishlist = participantsPath.GetProperty("post");
        var getCurrentParticipant = paths
            .GetProperty("/api/v1/shared-wishlists/{shareLinkId}/participants/current")
            .GetProperty("get");
        var reservationsPath = paths
            .GetProperty("/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/reservations/current");
        var getCurrentReservation = reservationsPath.GetProperty("get");
        var upsertCurrentReservation = reservationsPath.GetProperty("put");
        var cancelCurrentReservation = reservationsPath.GetProperty("delete");

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
        AssertOptionalBearerAndGuestCookie(
            getSharedWishlist,
            expectsAntiforgery: false);
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
        var availableOnly = Assert.Single(
            getSharedWishlist.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "availableOnly");
        Assert.Equal(
            "query",
            availableOnly.GetProperty("in").GetString());
        Assert.Equal(
            "Whether to return only gifts available to the current participant.",
            availableOnly.GetProperty("description").GetString());
        Assert.False(availableOnly.TryGetProperty(
            "required",
            out var availableOnlyIsRequired) &&
            availableOnlyIsRequired.GetBoolean());
        var availableOnlySchema = ResolveSchema(
            document.RootElement,
            availableOnly.GetProperty("schema"));
        Assert.Equal(
            "boolean",
            availableOnlySchema.GetProperty("type").GetString());
        AssertResponses(
            getSharedWishlist,
            "200",
            "400",
            "401",
            "404",
            "429",
            "500",
            "503");
        AssertSharedSuccessResponse(
            document.RootElement,
            getSharedWishlist);

        Assert.Equal(
            "Gets detailed information about one gift wish through an active share link.",
            getSharedWish.GetProperty("summary").GetString());
        AssertOptionalBearerAndGuestCookie(
            getSharedWish,
            expectsAntiforgery: false);
        var wishId = Assert.Single(
            getSharedWish.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "wishId");
        Assert.Equal(
            "path",
            wishId.GetProperty("in").GetString());
        Assert.True(wishId.GetProperty("required").GetBoolean());
        AssertResponses(
            getSharedWish,
            "200",
            "400",
            "401",
            "404",
            "429",
            "500",
            "503");
        AssertSharedWishSuccessResponse(
            document.RootElement,
            getSharedWish);

        Assert.Equal(
            "Joins a wishlist through its active share link.",
            joinSharedWishlist.GetProperty("summary").GetString());
        AssertOptionalBearerAndGuestCookie(
            joinSharedWishlist,
            expectsAntiforgery: true);
        var requestBody = joinSharedWishlist.GetProperty("requestBody");
        Assert.False(requestBody.TryGetProperty(
            "required",
            out var requestBodyIsRequired) && requestBodyIsRequired.GetBoolean());
        AssertResponses(
            joinSharedWishlist,
            "200",
            "201",
            "400",
            "401",
            "404",
            "409",
            "413",
            "415",
            "429",
            "500",
            "503");
        AssertParticipantSuccessResponse(
            document.RootElement,
            joinSharedWishlist,
            "200",
            includesLocation: false,
            includesGuestCookie: false);
        AssertParticipantSuccessResponse(
            document.RootElement,
            joinSharedWishlist,
            "201",
            includesLocation: true,
            includesGuestCookie: true);

        Assert.Equal(
            "Gets the participant associated with the current caller.",
            getCurrentParticipant.GetProperty("summary").GetString());
        AssertOptionalBearerAndGuestCookie(
            getCurrentParticipant,
            expectsAntiforgery: false);
        AssertResponses(
            getCurrentParticipant,
            "200",
            "401",
            "404",
            "429",
            "500",
            "503");
        AssertParticipantSuccessResponse(
            document.RootElement,
            getCurrentParticipant,
            "200",
            includesLocation: false,
            includesGuestCookie: false);

        Assert.Equal(
            "Gets the current participant's reservation for one shared gift.",
            getCurrentReservation.GetProperty("summary").GetString());
        AssertOptionalBearerAndGuestCookie(
            getCurrentReservation,
            expectsAntiforgery: false);
        AssertResponses(
            getCurrentReservation,
            "200",
            "400",
            "401",
            "404",
            "429",
            "500",
            "503");
        AssertReservationSuccessResponse(
            document.RootElement,
            getCurrentReservation,
            "200",
            includesLocation: false);

        Assert.Equal(
            "Creates or replaces the current participant's reservation for one shared gift.",
            upsertCurrentReservation.GetProperty("summary").GetString());
        AssertOptionalBearerAndGuestCookie(
            upsertCurrentReservation,
            expectsAntiforgery: true);
        var optionalIfMatch = Assert.Single(
            upsertCurrentReservation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "If-Match");
        Assert.False(optionalIfMatch.TryGetProperty(
            "required",
            out var optionalIfMatchIsRequired) &&
            optionalIfMatchIsRequired.GetBoolean());
        var reservationRequest = ResolveSchema(
            document.RootElement,
            upsertCurrentReservation
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            ["quantity"],
            reservationRequest
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name));
        AssertResponses(
            upsertCurrentReservation,
            "200",
            "201",
            "400",
            "401",
            "404",
            "409",
            "412",
            "413",
            "415",
            "428",
            "429",
            "500",
            "503");
        AssertReservationSuccessResponse(
            document.RootElement,
            upsertCurrentReservation,
            "200",
            includesLocation: false);
        AssertReservationSuccessResponse(
            document.RootElement,
            upsertCurrentReservation,
            "201",
            includesLocation: true);

        Assert.Equal(
            "Cancels the current participant's reservation for one shared gift.",
            cancelCurrentReservation.GetProperty("summary").GetString());
        AssertOptionalBearerAndGuestCookie(
            cancelCurrentReservation,
            expectsAntiforgery: true);
        AssertRequiredIfMatch(cancelCurrentReservation);
        AssertResponses(
            cancelCurrentReservation,
            "204",
            "400",
            "401",
            "404",
            "412",
            "428",
            "429",
            "500",
            "503");
        var cancellationSuccess = cancelCurrentReservation
            .GetProperty("responses")
            .GetProperty("204");
        Assert.True(cancellationSuccess
            .GetProperty("headers")
            .TryGetProperty(
                "Cache-Control",
                out _));
        Assert.False(cancellationSuccess
            .GetProperty("headers")
            .TryGetProperty(
                "ETag",
                out _));
        Assert.False(cancellationSuccess.TryGetProperty(
            "content",
            out _));
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

    private static void AssertOptionalBearerAndGuestCookie(
        JsonElement operation,
        bool expectsAntiforgery)
    {
        Assert.Equal(
            2,
            operation.GetProperty("security").GetArrayLength());
        var parameters = operation.GetProperty("parameters").EnumerateArray();
        var shareToken = Assert.Single(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "X-MonKado-Share-Token");
        Assert.False(shareToken.TryGetProperty(
            "required",
            out var shareTokenIsRequired) && shareTokenIsRequired.GetBoolean());
        var guestCookie = Assert.Single(
            operation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "__Host-MonKado.Guest");
        Assert.Equal(
            "cookie",
            guestCookie.GetProperty("in").GetString());
        Assert.False(guestCookie.TryGetProperty(
            "required",
            out var guestCookieIsRequired) && guestCookieIsRequired.GetBoolean());
        Assert.Equal(
            expectsAntiforgery,
            operation
                .GetProperty("parameters")
                .EnumerateArray()
                .Any(parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN"));
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

    private static void AssertReservationSuccessResponse(
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
                "quantity",
                "updatedAt",
                "wishId"
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
                "currentParticipant",
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
        var wishSchema = ResolveSchema(
            document,
            schema
                .GetProperty("properties")
                .GetProperty("wishes")
                .GetProperty("items"));
        Assert.Equal(
            [
                "availableQuantity",
                "currentParticipantReservedQuantity",
                "id",
                "name",
                "price",
                "quantity",
                "reservedQuantity",
                "url"
            ],
            wishSchema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(property => property));
    }

    private static void AssertParticipantSuccessResponse(
        JsonElement document,
        JsonElement operation,
        string statusCode,
        bool includesLocation,
        bool includesGuestCookie)
    {
        var success = operation
            .GetProperty("responses")
            .GetProperty(statusCode);
        var headers = success.GetProperty("headers");
        Assert.True(headers.TryGetProperty(
            "Cache-Control",
            out _));
        Assert.Equal(
            includesLocation,
            headers.TryGetProperty(
                "Location",
                out _));
        Assert.Equal(
            includesGuestCookie,
            headers.TryGetProperty(
                "Set-Cookie",
                out _));
        var schema = ResolveSchema(
            document,
            success
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "displayName",
                "id"
            ],
            schema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(property => property));
    }

    private static void AssertSharedWishSuccessResponse(
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
                "availableQuantity",
                "currentParticipantReservedQuantity",
                "id",
                "name",
                "note",
                "price",
                "quantity",
                "reservedQuantity",
                "url"
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
