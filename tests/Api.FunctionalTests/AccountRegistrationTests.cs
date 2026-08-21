using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class AccountRegistrationTests
{
    [Fact]
    public async Task RegisterAsync_WhenValidRequest_ReturnsEmptyAcceptedResponseWithoutSessionCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        // Act
        using var response = await SendRegistrationAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.Equal(
            0,
            response.Content.Headers.ContentLength);
        Assert.True(response.Headers.CacheControl?.NoStore);

        if (response.Headers.TryGetValues(
            "Set-Cookie",
            out var cookies))
        {
            Assert.DoesNotContain(
                cookies,
                value => value.Contains(
                    "Identity",
                    StringComparison.OrdinalIgnoreCase));
        }
        var call = Assert.Single(factory.RegistrationService.Calls);
        Assert.Equal(
            "Lea@example.fr",
            call.Email);
        Assert.Equal(
            " a secure password ",
            call.Password);
        Assert.Equal(
            "Léa",
            call.DisplayName);
    }

    [Fact]
    public async Task RegisterAsync_WhenInvalidPayload_ReturnsErrorResponse()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(new
            {
                email = "invalid",
                password = "short",
                displayName = "\n"
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The validation problem response is empty.");
        var root = document.RootElement;
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            root.GetProperty("errorCode").GetString());
        Assert.Equal(
            400,
            root.GetProperty("statusCode").GetInt32());
        var properties = root
            .GetProperty("validationErrors")
            .EnumerateArray()
            .Select(error => error.GetProperty("propertyName").GetString())
            .ToArray();
        Assert.Contains(
            "email",
            properties);
        Assert.Contains(
            "password",
            properties);
        Assert.Contains(
            "displayName",
            properties);
        Assert.Equal(
            0,
            factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task RegisterAsync_WhenMissingCsrfToken_IsRejectedBeforeRegistration()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/registrations",
            ValidPayload(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            0,
            factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task RegisterAsync_WhenMalformedJson_ReturnsErrorResponse()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/registrations")
        {
            Content = new StringContent(
                "{",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The validation problem response is empty.");
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            0,
            factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task RegisterAsync_WhenNonJsonContent_ReturnsUnsupportedMediaType()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/registrations")
        {
            Content = new StringContent(
                "not-json",
                Encoding.UTF8,
                "text/plain")
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        Assert.Equal(
            0,
            factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task RegisterAsync_WhenSensitiveRegistrationValues_AreNeverWrittenToLogs()
    {
        // Arrange
        const string email = "sensitive-email@example.fr";
        const string password = "sensitive-password-value";
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(new
            {
                email,
                password,
                displayName = "Private"
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                email,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                password,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegisterAsync_WhenSixthRequestWithinOneMinute_IsRateLimited()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        for (var requestNumber = 1; requestNumber <= 5; requestNumber++)
        {
            using var accepted = await SendRegistrationAsync(client);
            Assert.Equal(
                HttpStatusCode.Accepted,
                accepted.StatusCode);
        }

        // Act
        using var rejected = await SendRegistrationAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var document = await rejected.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The rate-limit response is empty.");
        Assert.Equal(
            ErrorCodes.RequestRateLimitExceeded,
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            5,
            factory.RegistrationService.CallCount);
    }

    [Fact]
    public async Task RegisterAsync_WhenUnavailablePostgreSql_ReturnsServiceUnavailableProblem()
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(ValidPayload())
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The dependency problem response is empty.");
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task RegisterAsync_WhenOpenApi_DocumentsRegistrationWithoutIdempotencyKey()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response is empty.");

        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/registrations")
            .GetProperty("post");

        // Assert
        Assert.DoesNotContain(
            operation.TryGetProperty(
                "parameters",
                out var parameters)
                ? parameters.EnumerateArray()
                : [],
            parameter => string.Equals(
                parameter.GetProperty("name").GetString(),
                "Idempotency-Key",
                StringComparison.OrdinalIgnoreCase));
        var csrfParameter = Assert.Single(
            operation.GetProperty("parameters").EnumerateArray(),
            parameter => string.Equals(
                parameter.GetProperty("name").GetString(),
                WebSecurityOptions.AntiforgeryHeaderName,
                StringComparison.Ordinal));
        Assert.Equal(
            "header",
            csrfParameter.GetProperty("in").GetString());
        Assert.True(csrfParameter.GetProperty("required").GetBoolean());
    }

    private static async Task<HttpResponseMessage> SendRegistrationAsync(HttpClient client)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(ValidPayload())
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static object ValidPayload()
    {

        return new
        {
            email = " Lea@example.fr ",
            password = " a secure password ",
            displayName = " Léa "
        };
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");

        return payload.Token;
    }
}
