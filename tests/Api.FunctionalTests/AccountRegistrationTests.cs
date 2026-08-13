using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JennGllg.Fr.MonKado.Back.Api.Security;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public sealed class AccountRegistrationTests
{
    [Fact]
    public async Task ValidRequestReturnsEmptyAcceptedResponseWithoutSessionCookie()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await SendRegistration(client);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength);
        Assert.True(response.Headers.CacheControl?.NoStore);
        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            Assert.DoesNotContain(
                cookies,
                value => value.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        }
        RegistrationCall call = Assert.Single(factory.RegistrationService.Calls);
        Assert.Equal("Lea@example.fr", call.Email);
        Assert.Equal(" a secure password ", call.Password);
        Assert.Equal("Léa", call.DisplayName);
    }

    [Fact]
    public async Task InvalidPayloadReturnsRfc9457ValidationProblem()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(new
            {
                email = "invalid",
                password = "short",
                displayName = "\n"
            })
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The validation problem response is empty.");
        JsonElement root = document.RootElement;
        Assert.Equal("VALIDATION_ERROR", root.GetProperty("code").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("/api/v1/auth/registrations", root.GetProperty("instance").GetString());
        Assert.True(root.GetProperty("errors").TryGetProperty("email", out _));
        Assert.True(root.GetProperty("errors").TryGetProperty("password", out _));
        Assert.True(root.GetProperty("errors").TryGetProperty("displayName", out _));
        Assert.Equal(0, factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task MissingCsrfTokenIsRejectedBeforeRegistration()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/registrations",
            ValidPayload(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task MalformedJsonReturnsRfc9457ValidationProblem()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/registrations")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The validation problem response is empty.");
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task NonJsonContentReturnsUnsupportedMediaType()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/registrations")
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task SensitiveRegistrationValuesAreNeverWrittenToLogs()
    {
        const string email = "sensitive-email@example.fr";
        const string password = "sensitive-password-value";
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(new { email, password, displayName = "Private" })
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(factory.LogMessages, message => message.Contains(email, StringComparison.Ordinal));
        Assert.DoesNotContain(factory.LogMessages, message => message.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SixthRequestWithinOneMinuteIsRateLimited()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        for (int requestNumber = 1; requestNumber <= 5; requestNumber++)
        {
            using HttpResponseMessage accepted = await SendRegistration(client);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        using HttpResponseMessage rejected = await SendRegistration(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using JsonDocument document = await rejected.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The rate-limit response is empty.");
        Assert.Equal("RATE_LIMIT_EXCEEDED", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(5, factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task UnavailablePostgreSqlReturnsServiceUnavailableProblem()
    {
        await using UnavailablePostgreSqlApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(ValidPayload())
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The dependency problem response is empty.");
        Assert.Equal("DEPENDENCY_UNAVAILABLE", document.RootElement.GetProperty("code").GetString());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task OpenApiDocumentsRegistrationWithoutIdempotencyKey()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using JsonDocument document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response is empty.");

        JsonElement operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/registrations")
            .GetProperty("post");

        Assert.DoesNotContain(
            operation.TryGetProperty("parameters", out JsonElement parameters)
                ? parameters.EnumerateArray()
                : [],
            parameter => string.Equals(
                parameter.GetProperty("name").GetString(),
                "Idempotency-Key",
                StringComparison.OrdinalIgnoreCase));
        JsonElement csrfParameter = Assert.Single(
            operation.GetProperty("parameters").EnumerateArray(),
            parameter => string.Equals(
                parameter.GetProperty("name").GetString(),
                WebSecurityOptions.AntiforgeryHeaderName,
                StringComparison.Ordinal));
        Assert.Equal("header", csrfParameter.GetProperty("in").GetString());
        Assert.True(csrfParameter.GetProperty("required").GetBoolean());
    }

    private static async Task<HttpResponseMessage> SendRegistration(HttpClient client)
    {
        string csrfToken = await GetCsrfToken(client);
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(ValidPayload())
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static object ValidPayload() => new
    {
        email = " Lea@example.fr ",
        password = " a secure password ",
        displayName = " Léa "
    };

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
