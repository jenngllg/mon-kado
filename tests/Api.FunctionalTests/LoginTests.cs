using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class LoginTests
{
    [Fact]
    public async Task LoginAsync_WhenValidRequest_ReturnsAccessTokenAndPreservesCredentials()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            " Lea@example.fr ",
            "  exact password  ",
            rememberMe: true);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The access token response is empty.");
        var payload = document.RootElement;
        Assert.Equal(
            [
                "accessToken",
                "expiresIn",
                "tokenType"
            ],
            payload.EnumerateObject()
                .Select(property => property.Name)
                .Order()
                .ToArray());
        Assert.Equal(
            "functional-access-token",
            payload.GetProperty("accessToken").GetString());
        Assert.Equal(
            "Bearer",
            payload.GetProperty("tokenType").GetString());
        Assert.Equal(
            900,
            payload.GetProperty("expiresIn").GetInt32());
        var refreshCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=functional-refresh-token;",
                StringComparison.Ordinal));
        Assert.Contains(
            "expires=",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "httponly",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "samesite=strict",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "domain=",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "secure",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        var call = Assert.Single(factory.SessionService.Calls);
        Assert.Equal(
            "Lea@example.fr",
            call.Email);
        Assert.Equal(
            "  exact password  ",
            call.Password);
        Assert.True(call.RememberMe);
        Assert.Null(call.CurrentRefreshToken);
    }

    [Theory]
    [InlineData(
        AccountLoginResult.InvalidCredentials,
        ErrorCodes.AccountInvalidCredentials)]
    [InlineData(
        AccountLoginResult.EmailNotConfirmed,
        ErrorCodes.AccountEmailNotConfirmed)]
    public async Task LoginAsync_WhenFailedLogin_ReturnsExpectedGenericProblem(
        AccountLoginResult result,
        string expectedCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.SessionService.Result = result;
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            "password",
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The authentication problem response is empty.");
        Assert.Equal(
            expectedCode,
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task LoginAsync_WhenInvalidPayload_ReturnsValidationProblemWithoutCallingService()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            "invalid",
            new string(
                'a',
                129),
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The validation problem response is empty.");
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(factory.SessionService.Calls);
    }

    [Fact]
    public async Task LoginAsync_WhenMissingCsrfToken_IsRejected()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/sessions",
            new
            {
                email = "lea@example.fr",
                password = "password"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.SessionService.Calls);
    }

    [Fact]
    public async Task LoginAsync_WhenNonJsonAndOversizedBodies_AreRejected()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);

        using var nonJsonRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions")
        {
            Content = new StringContent(
                "not-json",
                Encoding.UTF8,
                "text/plain")
        };
        nonJsonRequest.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);
        using var nonJson = await client.SendAsync(
            nonJsonRequest,
            TestContext.Current.CancellationToken);

        using var oversizedRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions")
        {
            Content = new StringContent(
                "{\"value\":\"" + new string(
                    'a',
                    5 * 1024) + "\"}",
                Encoding.UTF8,
                "application/json")
        };
        oversizedRequest.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);
        // Act
        using var oversized = await client.SendAsync(
            oversizedRequest,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            nonJson.StatusCode);
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            oversized.StatusCode);
        Assert.Empty(factory.SessionService.Calls);
    }

    [Fact]
    public async Task LoginAsync_WhenEleventhRequestWithinOneMinute_IsRateLimited()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using var accepted = await LoginAsync(
                client,
                "lea@example.fr",
                "password",
                rememberMe: false);
            Assert.Equal(
                HttpStatusCode.OK,
                accepted.StatusCode);
        }

        // Act
        using var rejected = await LoginAsync(
            client,
            "lea@example.fr",
            "password",
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            10,
            factory.SessionService.Calls.Count);
    }

    [Fact]
    public async Task LoginAsync_WhenSensitiveLoginValues_AreNeverWrittenToLogs()
    {
        // Arrange
        const string Email = "sensitive-login@example.fr";
        const string Password = "sensitive-login-password";
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            Email,
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.DoesNotContain(
            factory.LogMessages,
            message =>
            message.Contains(
                Email,
                StringComparison.Ordinal) ||
            message.Contains(
                Password,
                StringComparison.Ordinal) ||
            message.Contains(
                "functional-access-token",
                StringComparison.Ordinal) ||
            message.Contains(
                "functional-refresh-token",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoginAsync_WhenUnavailablePostgreSql_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            "password",
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task RefreshAsync_WhenCookieIsValid_ReturnsAccessTokenAndRotatesCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            "password",
            rememberMe: false);

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The access token response is empty.");
        Assert.Equal(
            "functional-access-token",
            payload.AccessToken);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=functional-refresh-token;",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_WhenEleventhRequestWithinOneMinute_IsRateLimited()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            "password",
            rememberMe: false);

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using var accepted = await RefreshAsync(client);
            Assert.Equal(
                HttpStatusCode.OK,
                accepted.StatusCode);
        }

        // Act
        using var rejected = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            10,
            factory.SessionService.RefreshCallCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenCookieIsMissing_ReturnsUnauthorizedAndDeletesCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The authentication error response is empty.");
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_WhenCsrfTokenIsMissing_ReturnsBadRequest()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            "password",
            rememberMe: false);

        // Act
        using var response = await client.PostAsync(
            "/api/v1/auth/sessions/refresh",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task RefreshAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailableAndPreservesCookie()
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        var refreshToken = $"{Guid.NewGuid():N}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions/refresh");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);
        request.Headers.Add(
            "Cookie",
            $"MonKado.Refresh={refreshToken}");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.TryGetValues(
            "Set-Cookie",
            out var cookies) && cookies.Any(value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LoginAsync_WhenOpenApi_DocumentsOptionalRememberMeAndCsrfHeader()
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
            .GetProperty("/api/v1/auth/sessions")
            .GetProperty("post");
        var schema = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        if (schema.TryGetProperty(
            "$ref",
            out var reference))
        {
            var schemaName = reference.GetString()?.Split('/').Last()
                ?? throw new InvalidOperationException("The login schema reference is invalid.");
            schema = document.RootElement.GetProperty("components").GetProperty("schemas")
                .GetProperty(schemaName);
        }


        // Assert
        Assert.Contains(
            operation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() ==
                WebSecurityOptions.AntiforgeryHeaderName);
        Assert.True(schema.GetProperty("properties").TryGetProperty(
            "rememberMe",
            out _));

        if (schema.TryGetProperty(
            "required",
            out var required))
        {
            Assert.DoesNotContain(
                required.EnumerateArray(),
                property => property.GetString() == "rememberMe");
        }
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password,
        bool rememberMe)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions")
        {
            Content = JsonContent.Create(new
            {
                email,
                password,
                rememberMe
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions/refresh");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
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
