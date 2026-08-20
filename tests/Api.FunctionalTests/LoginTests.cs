using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JennGllg.Fr.MonKado.Back.Api.Security;
using JennGllg.Fr.MonKado.Back.Application.Accounts;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public sealed class LoginTests
{
    [Fact]
    public async Task ValidRequestReturnsEmptyNoContentAndPreservesCredentials()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Login(
            client,
            " Lea@example.fr ",
            "  exact password  ",
            rememberMe: true);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength);
        Assert.True(response.Headers.CacheControl?.NoStore);
        LoginCall call = Assert.Single(factory.SessionService.Calls);
        Assert.Equal("Lea@example.fr", call.Email);
        Assert.Equal("  exact password  ", call.Password);
        Assert.True(call.RememberMe);
    }

    [Theory]
    [InlineData(AccountLoginResult.InvalidCredentials, "INVALID_CREDENTIALS")]
    [InlineData(AccountLoginResult.EmailNotConfirmed, "EMAIL_NOT_CONFIRMED")]
    public async Task FailedLoginReturnsExpectedGenericProblem(
        AccountLoginResult result,
        string expectedCode)
    {
        await using RegistrationApiFactory factory = new();
        factory.SessionService.Result = result;
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Login(
            client,
            "lea@example.fr",
            "password",
            rememberMe: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The authentication problem response is empty.");
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidPayloadReturnsValidationProblemWithoutCallingService()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Login(
            client,
            "invalid",
            new string('a', 129),
            rememberMe: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The validation problem response is empty.");
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("code").GetString());
        Assert.Empty(factory.SessionService.Calls);
    }

    [Fact]
    public async Task MissingCsrfTokenIsRejected()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/sessions",
            new { email = "lea@example.fr", password = "password" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.SessionService.Calls);
    }

    [Fact]
    public async Task NonJsonAndOversizedBodiesAreRejected()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string csrfToken = await GetCsrfToken(client);

        using HttpRequestMessage nonJsonRequest = new(HttpMethod.Post, "/api/v1/auth/sessions")
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
        };
        nonJsonRequest.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);
        using HttpResponseMessage nonJson = await client.SendAsync(
            nonJsonRequest,
            TestContext.Current.CancellationToken);

        using HttpRequestMessage oversizedRequest = new(HttpMethod.Post, "/api/v1/auth/sessions")
        {
            Content = new StringContent(
                "{\"value\":\"" + new string('a', 5 * 1024) + "\"}",
                Encoding.UTF8,
                "application/json")
        };
        oversizedRequest.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);
        using HttpResponseMessage oversized = await client.SendAsync(
            oversizedRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, nonJson.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Empty(factory.SessionService.Calls);
    }

    [Fact]
    public async Task EleventhRequestWithinOneMinuteIsRateLimited()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        for (int requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using HttpResponseMessage accepted = await Login(
                client,
                "lea@example.fr",
                "password",
                rememberMe: false);
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }

        using HttpResponseMessage rejected = await Login(
            client,
            "lea@example.fr",
            "password",
            rememberMe: false);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(10, factory.SessionService.Calls.Count);
    }

    [Fact]
    public async Task SensitiveLoginValuesAreNeverWrittenToLogs()
    {
        const string Email = "sensitive-login@example.fr";
        const string Password = "sensitive-login-password";
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Login(client, Email, Password, rememberMe: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(factory.LogMessages, message =>
            message.Contains(Email, StringComparison.Ordinal) ||
            message.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnavailablePostgreSqlReturnsServiceUnavailable()
    {
        await using UnavailablePostgreSqlApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Login(
            client,
            "lea@example.fr",
            "password",
            rememberMe: false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task OpenApiDocumentsOptionalRememberMeAndCsrfHeader()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using JsonDocument document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response is empty.");

        JsonElement operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/sessions")
            .GetProperty("post");
        JsonElement schema = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        if (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            string schemaName = reference.GetString()?.Split('/').Last()
                ?? throw new InvalidOperationException("The login schema reference is invalid.");
            schema = document.RootElement.GetProperty("components").GetProperty("schemas")
                .GetProperty(schemaName);
        }


        Assert.Contains(
            operation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() ==
                WebSecurityOptions.AntiforgeryHeaderName);
        Assert.True(schema.GetProperty("properties").TryGetProperty("rememberMe", out _));
        if (schema.TryGetProperty("required", out JsonElement required))
        {
            Assert.DoesNotContain(
                required.EnumerateArray(),
                property => property.GetString() == "rememberMe");
        }
    }

    private static async Task<HttpResponseMessage> Login(
        HttpClient client,
        string email,
        string password,
        bool rememberMe)
    {
        string csrfToken = await GetCsrfToken(client);
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/sessions")
        {
            Content = JsonContent.Create(new { email, password, rememberMe })
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetCsrfToken(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        CsrfTokenResponse payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
        return payload.Token;
    }
}
