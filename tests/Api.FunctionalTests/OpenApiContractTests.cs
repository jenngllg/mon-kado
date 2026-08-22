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
            expectsRefreshCookie: false);
        AssertTokenOperation(
            refresh,
            expectsRefreshCookie: true);
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

    private static void AssertTokenOperation(
        JsonElement operation,
        bool expectsRefreshCookie)
    {
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Contains(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");

        if (expectsRefreshCookie)
        {
            var refreshCookie = Assert.Single(
                parameters,
                parameter => parameter.GetProperty("name").GetString() ==
                    "__Host-MonKado.Refresh");
            Assert.Equal(
                "cookie",
                refreshCookie.GetProperty("in").GetString());
            Assert.True(refreshCookie.GetProperty("required").GetBoolean());
            Assert.Contains(
                "MonKado.Refresh",
                refreshCookie.GetProperty("description").GetString(),
                StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(
                parameters,
                parameter => parameter.GetProperty("in").GetString() == "cookie");
        }

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
}
