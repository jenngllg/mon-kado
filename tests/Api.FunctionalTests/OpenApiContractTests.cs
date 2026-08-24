using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class OpenApiContractTests(UnavailablePostgreSqlApiFactory factory) : IClassFixture<UnavailablePostgreSqlApiFactory>
{
    private readonly UnavailablePostgreSqlApiFactory _factory = factory;

    [Fact]
    public async Task GetAsync_WhenGetV1Contract_ReturnsOpenApi31Document()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response body is empty.");
        var root = document.RootElement;

        Assert.Equal(
            "3.1.1",
            root.GetProperty("openapi").GetString());
        Assert.Equal(
            "Mon Kado API",
            root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal(
            "v1",
            root.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal(
            JsonValueKind.Object,
            root.GetProperty("paths").ValueKind);
        var bearer = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        Assert.Equal(
            "http",
            bearer.GetProperty("type").GetString());
        Assert.Equal(
            "bearer",
            bearer.GetProperty("scheme").GetString());
        Assert.Equal(
            "JWT",
            bearer.GetProperty("bearerFormat").GetString());

        var login = root
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/sessions")
            .GetProperty("post");
        var refresh = root
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/sessions/refresh")
            .GetProperty("post");
        AssertTokenOperation(
            login,
            isRefreshCookieRequired: false);
        AssertTokenOperation(
            refresh,
            isRefreshCookieRequired: true);
    }

    [Fact]
    public async Task GetAsync_WhenGetUnknownDocument_ReturnsNotFound()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/openapi/v2.json",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task GetAsync_WhenCurrentSessionIsDocumented_RequiresBearerAndExposesExpectedContract()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response body is empty.");
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/sessions/current")
            .GetProperty("get");

        // Assert
        Assert.Equal(
            "Gets the current authenticated member session from persistence.",
            operation.GetProperty("summary").GetString());
        var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty(
            "Bearer",
            out _));
        Assert.False(operation.TryGetProperty(
            "parameters",
            out var parameters) && parameters.EnumerateArray().Any(parameter =>
                parameter.GetProperty("name").GetString() is
                    "X-CSRF-TOKEN" or "__Host-MonKado.Refresh"));
        var responses = operation.GetProperty("responses");
        Assert.True(responses.TryGetProperty(
            "200",
            out var success));
        Assert.True(responses.TryGetProperty(
            "401",
            out _));
        Assert.True(responses.TryGetProperty(
            "403",
            out _));
        Assert.True(responses.TryGetProperty(
            "500",
            out _));
        Assert.True(responses.TryGetProperty(
            "503",
            out _));
        Assert.True(success.GetProperty("headers").TryGetProperty(
            "ETag",
            out _));
        var schema = success
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        if (schema.TryGetProperty(
            "$ref",
            out var reference))
        {
            var schemaName = reference.GetString()?.Split('/').Last()
                ?? throw new InvalidOperationException("The current session schema reference is invalid.");
            schema = document.RootElement
                .GetProperty("components")
                .GetProperty("schemas")
                .GetProperty(schemaName);
        }

        var properties = schema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(property => property)
            .ToArray();
        Assert.Equal(
            [
                "displayName",
                "email",
                "id",
                "roles"
            ],
            properties);
        Assert.Equal(
            "array",
            schema
                .GetProperty("properties")
                .GetProperty("roles")
                .GetProperty("type")
                .GetString());
    }

    [Fact]
    public async Task GetAsync_WhenMemberProfileUpdateIsDocumented_ExposesOptimisticConcurrencyContract()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response body is empty.");
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/members/current/profile")
            .GetProperty("put");

        // Assert
        Assert.Equal(
            "Updates the display name of the current authenticated member.",
            operation.GetProperty("summary").GetString());
        var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty(
            "Bearer",
            out _));
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        var ifMatch = Assert.Single(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "If-Match");
        Assert.Equal(
            "header",
            ifMatch.GetProperty("in").GetString());
        Assert.True(ifMatch.GetProperty("required").GetBoolean());
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        var responses = operation.GetProperty("responses");

        foreach (var statusCode in new[]
        {
            "200",
            "400",
            "401",
            "403",
            "412",
            "413",
            "415",
            "428",
            "500",
            "503"
        })
        {
            Assert.True(
                responses.TryGetProperty(
                    statusCode,
                    out _),
                $"Response {statusCode} is missing.");
        }

        var success = responses.GetProperty("200");
        Assert.True(success.GetProperty("headers").TryGetProperty(
            "ETag",
            out _));
        var responseSchema = ResolveSchema(
            document.RootElement,
            success
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            ["displayName"],
            responseSchema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        var requestSchema = ResolveSchema(
            document.RootElement,
            operation
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            ["displayName"],
            requestSchema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
    }

    [Fact]
    public async Task GetAsync_WhenLogoutIsDocumented_ExposesAnonymousIdempotentContract()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response body is empty.");
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/sessions/current")
            .GetProperty("delete");

        // Assert
        Assert.Equal(
            "Ends the current browser refresh session.",
            operation.GetProperty("summary").GetString());
        Assert.False(operation.TryGetProperty(
            "security",
            out var security) && security.GetArrayLength() > 0);
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        var antiforgery = Assert.Single(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        Assert.True(antiforgery.GetProperty("required").GetBoolean());
        var refreshCookie = Assert.Single(
            parameters,
            parameter => parameter.GetProperty("name").GetString() ==
                "__Host-MonKado.Refresh");
        Assert.Equal(
            "cookie",
            refreshCookie.GetProperty("in").GetString());
        Assert.False(refreshCookie.TryGetProperty(
            "required",
            out var required) && required.GetBoolean());
        var responses = operation.GetProperty("responses");
        Assert.True(responses.TryGetProperty(
            "204",
            out var success));
        Assert.True(responses.TryGetProperty(
            "400",
            out _));
        Assert.True(responses.TryGetProperty(
            "429",
            out _));
        Assert.True(responses.TryGetProperty(
            "500",
            out _));
        Assert.True(responses.TryGetProperty(
            "503",
            out _));
        Assert.False(responses.TryGetProperty(
            "401",
            out _));
        Assert.False(responses.TryGetProperty(
            "403",
            out _));
        var headers = success.GetProperty("headers");
        Assert.Contains(
            "Deletes",
            headers.GetProperty("Set-Cookie").GetProperty("description").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "no-store",
            headers.GetProperty("Cache-Control").GetProperty("description").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_WhenMemberEmailChangeIsDocumented_ExposesSecureTwoStepContract()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response body is empty.");
        var paths = document.RootElement.GetProperty("paths");
        var requestOperation = paths
            .GetProperty("/api/v1/members/current/email")
            .GetProperty("put");
        var confirmationOperation = paths
            .GetProperty("/api/v1/auth/email-change-confirmations")
            .GetProperty("post");

        // Assert
        Assert.Equal(
            "Requests a change to the current authenticated member email address.",
            requestOperation.GetProperty("summary").GetString());
        Assert.True(Assert.Single(
            requestOperation.GetProperty("security").EnumerateArray())
            .TryGetProperty(
                "Bearer",
                out _));
        var requestParameters = requestOperation
            .GetProperty("parameters")
            .EnumerateArray()
            .ToArray();
        var ifMatch = Assert.Single(
            requestParameters,
            parameter => parameter.GetProperty("name").GetString() == "If-Match");
        Assert.True(ifMatch.GetProperty("required").GetBoolean());
        Assert.DoesNotContain(
            requestParameters,
            parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        var requestResponses = requestOperation.GetProperty("responses");

        foreach (var statusCode in new[]
        {
            "202",
            "400",
            "401",
            "403",
            "409",
            "412",
            "413",
            "415",
            "428",
            "429",
            "500",
            "503"
        })
        {
            Assert.True(
                requestResponses.TryGetProperty(
                    statusCode,
                    out _),
                $"Response {statusCode} is missing.");
        }

        var requestSuccessHeaders = requestResponses
            .GetProperty("202")
            .GetProperty("headers");
        Assert.False(requestSuccessHeaders.TryGetProperty(
            "ETag",
            out _));
        Assert.Contains(
            "no-store",
            requestSuccessHeaders
                .GetProperty("Cache-Control")
                .GetProperty("description")
                .GetString(),
            StringComparison.Ordinal);
        AssertErrorResponseSchema(
            document.RootElement,
            requestResponses,
            "413");
        AssertErrorResponseSchema(
            document.RootElement,
            requestResponses,
            "415");
        var requestSchema = ResolveSchema(
            document.RootElement,
            requestOperation
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "currentPassword",
                "email"
            ],
            requestSchema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(property => property)
                .ToArray());

        Assert.Equal(
            "Confirms a pending member email change.",
            confirmationOperation.GetProperty("summary").GetString());
        Assert.False(confirmationOperation.TryGetProperty(
            "security",
            out var security) && security.GetArrayLength() > 0);
        var confirmationParameters = confirmationOperation
            .GetProperty("parameters")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(
            confirmationParameters,
            parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        var confirmationResponses = confirmationOperation.GetProperty("responses");
        var confirmationSuccess = confirmationResponses.GetProperty("204");
        Assert.True(confirmationSuccess
            .GetProperty("headers")
            .TryGetProperty(
                "Set-Cookie",
                out _));
        Assert.True(confirmationResponses.TryGetProperty(
            "409",
            out _));
        Assert.True(confirmationResponses.TryGetProperty(
            "503",
            out _));
        AssertErrorResponseSchema(
            document.RootElement,
            confirmationResponses,
            "413");
        AssertErrorResponseSchema(
            document.RootElement,
            confirmationResponses,
            "415");
    }

    [Fact]
    public async Task GetAsync_WhenMemberPasswordChangeIsDocumented_ExposesSecureContract()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response body is empty.");
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/members/current/password")
            .GetProperty("put");

        // Assert
        Assert.Equal(
            "Changes the password of the current authenticated member.",
            operation.GetProperty("summary").GetString());
        Assert.True(Assert.Single(
            operation.GetProperty("security").EnumerateArray())
            .TryGetProperty(
                "Bearer",
                out _));
        Assert.False(operation.TryGetProperty(
            "parameters",
            out var parameters) && parameters.EnumerateArray().Any(parameter =>
                parameter.GetProperty("name").GetString() is
                    "If-Match" or
                    "X-CSRF-TOKEN"));
        var responses = operation.GetProperty("responses");

        foreach (var statusCode in new[]
        {
            "204",
            "400",
            "401",
            "403",
            "413",
            "415",
            "429",
            "500",
            "503"
        })
        {
            Assert.True(
                responses.TryGetProperty(
                    statusCode,
                    out _),
                $"Response {statusCode} is missing.");
        }

        var successHeaders = responses
            .GetProperty("204")
            .GetProperty("headers");
        Assert.Contains(
            "Deletes",
            successHeaders
                .GetProperty("Set-Cookie")
                .GetProperty("description")
                .GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "no-store",
            successHeaders
                .GetProperty("Cache-Control")
                .GetProperty("description")
                .GetString(),
            StringComparison.Ordinal);
        var requestSchema = ResolveSchema(
            document.RootElement,
            operation
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal(
            [
                "currentPassword",
                "newPassword"
            ],
            requestSchema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(property => property)
                .ToArray());
        AssertErrorResponseSchema(
            document.RootElement,
            responses,
            "400");
    }

    [Fact]
    public async Task GetAsync_WhenAuthenticationJsonEndpointsAreDocumented_ExposesErrorSchemasForContentFailures()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response body is empty.");
        var paths = document.RootElement.GetProperty("paths");
        JsonElement[] operations =
        [
            paths.GetProperty("/api/v1/auth/registrations").GetProperty("post"),
            paths.GetProperty("/api/v1/auth/sessions").GetProperty("post"),
            paths.GetProperty("/api/v1/auth/email-confirmations").GetProperty("post"),
            paths.GetProperty("/api/v1/auth/email-confirmation-requests").GetProperty("post")
        ];

        // Assert

        foreach (var operation in operations)
        {
            var responses = operation.GetProperty("responses");
            AssertErrorResponseSchema(
                document.RootElement,
                responses,
                "413");
            AssertErrorResponseSchema(
                document.RootElement,
                responses,
                "415");
        }
    }

    private static void AssertErrorResponseSchema(
        JsonElement document,
        JsonElement responses,
        string statusCode)
    {
        var schema = ResolveSchema(
            document,
            responses
                .GetProperty(statusCode)
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
                .OrderBy(property => property)
                .ToArray());
    }

    private static void AssertTokenOperation(
        JsonElement operation,
        bool isRefreshCookieRequired)
    {
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Contains(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");

        var refreshCookie = Assert.Single(
            parameters,
            parameter => parameter.GetProperty("name").GetString() ==
                "__Host-MonKado.Refresh");
        Assert.Equal(
            "cookie",
            refreshCookie.GetProperty("in").GetString());
        var isRequired = refreshCookie.TryGetProperty(
            "required",
            out var required) && required.GetBoolean();
        Assert.Equal(
            isRefreshCookieRequired,
            isRequired);
        Assert.Contains(
            "MonKado.Refresh",
            refreshCookie.GetProperty("description").GetString(),
            StringComparison.Ordinal);

        var responses = operation.GetProperty("responses");
        Assert.True(responses.TryGetProperty(
            "200",
            out var success));
        Assert.True(responses.TryGetProperty(
            "400",
            out _));
        Assert.True(responses.TryGetProperty(
            "401",
            out _));
        Assert.True(responses.TryGetProperty(
            "429",
            out _));
        Assert.True(responses.TryGetProperty(
            "503",
            out _));
        var headers = success.GetProperty("headers");
        Assert.Contains(
            "__Host-MonKado.Refresh",
            headers.GetProperty("Set-Cookie").GetProperty("description").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "no-store",
            headers.GetProperty("Cache-Control").GetProperty("description").GetString(),
            StringComparison.Ordinal);
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
