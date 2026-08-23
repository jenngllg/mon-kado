using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class PasswordResetTests
{
    private static readonly string _validUserId = Guid.CreateVersion7().ToString("D");
    private const string ValidToken = "AbCd_-0123";
    private const string ValidPassword = "new secure password";

    [Fact]
    public async Task RequestAsync_WhenEmailIsValid_ReturnsIndistinguishableAcceptedResponse()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await RequestResetAsync(
            client,
            " Member@example.fr ");

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            0,
            response.Content.Headers.ContentLength);
        Assert.Equal(
            "Member@example.fr",
            Assert.Single(factory.PasswordResetService.RequestedEmails));
    }

    [Fact]
    public async Task RequestAsync_WhenEmailIsInvalid_ReturnsValidationErrorWithoutCallingService()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await RequestResetAsync(
            client,
            "invalid");

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error.ErrorCode);
        Assert.Empty(factory.PasswordResetService.RequestedEmails);
    }

    [Theory]
    [InlineData("Local", "MonKado.Refresh=;")]
    [InlineData("Production", "__Host-MonKado.Refresh=;")]
    public async Task ResetAsync_WhenLinkIsValid_ReturnsNoContentAndDeletesCookie(
        string environment,
        string expectedDeletedCookie)
    {
        // Arrange
        using var keys = new TemporaryKeyDirectory();
        await using var factory = new RegistrationApiFactory(
            environment,
            keys.Path);
        using var client = factory.CreateClient();
        client.BaseAddress = new Uri(
            environment == "Production"
                ? "https://localhost"
                : "http://localhost");
        using var request = await CreateResetRequestAsync(
            client,
            _validUserId,
            ValidToken,
            " new secure password ");
        request.Headers.Add(
            "Cookie",
            environment == "Production"
                ? "__Host-MonKado.Refresh=current-refresh-token"
                : "MonKado.Refresh=current-refresh-token");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            0,
            response.Content.Headers.ContentLength);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                expectedDeletedCookie,
                StringComparison.Ordinal));
        var call = Assert.Single(factory.PasswordResetService.ResetCalls);
        Assert.Equal(
            _validUserId,
            call.UserId);
        Assert.Equal(
            ValidToken,
            call.Token);
        Assert.Equal(
            " new secure password ",
            call.NewPassword);
    }

    [Theory]
    [InlineData("not-a-guid", "AbCd_-0123", "new secure password")]
    [InlineData("0198d027-51c0-7000-8000-000000000003", "invalid token", "new secure password")]
    public async Task ResetAsync_WhenLinkShapeIsInvalid_ReturnsGenericErrorWithoutCallingService(
        string userId,
        string token,
        string newPassword)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await ResetWithCookieAsync(
            client,
            userId,
            token,
            newPassword);

        // Assert
        await AssertInvalidResetAsync(response);
        Assert.Empty(factory.PasswordResetService.ResetCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ResetAsync_WhenNewPasswordIsInvalid_ReturnsValidationDetailsWithoutCallingService()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await ResetWithCookieAsync(
            client,
            _validUserId,
            ValidToken,
            "short");

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error.ErrorCode);
        var validationError = Assert.Single(error.ValidationErrors ?? []);
        Assert.Equal(
            "newPassword",
            validationError.PropertyName);
        Assert.Empty(factory.PasswordResetService.ResetCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ResetAsync_WhenServiceRejectsLink_ReturnsGenericErrorWithoutDeletingCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.PasswordResetService.ResetResult = false;
        using var client = factory.CreateClient();

        // Act
        using var response = await ResetWithCookieAsync(
            client,
            _validUserId,
            ValidToken,
            ValidPassword);

        // Assert
        await AssertInvalidResetAsync(response);
        Assert.Single(factory.PasswordResetService.ResetCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("request")]
    [InlineData("reset")]
    public async Task ExecuteAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailableWithoutDeletingCookie(
        string operation)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.PasswordResetService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = factory.CreateClient();

        // Act
        using var response = operation == "request"
            ? await RequestResetAsync(
                client,
                "member@example.fr")
            : await ResetWithCookieAsync(
                client,
                _validUserId,
                ValidToken,
                ValidPassword);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error.ErrorCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("/api/v1/auth/password-reset-requests")]
    [InlineData("/api/v1/auth/password-resets")]
    public async Task ExecuteAsync_WhenCsrfTokenIsMissing_ReturnsBadRequest(string requestUri)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            requestUri,
            new
            {
                email = "member@example.fr",
                userId = _validUserId,
                token = ValidToken,
                newPassword = ValidPassword
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.PasswordResetService.RequestedEmails);
        Assert.Empty(factory.PasswordResetService.ResetCalls);
    }

    [Theory]
    [InlineData("/api/v1/auth/password-reset-requests")]
    [InlineData("/api/v1/auth/password-resets")]
    public async Task ExecuteAsync_WhenContentTypeIsNotJson_ReturnsUnsupportedMediaType(string requestUri)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            requestUri)
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
        Assert.Empty(factory.PasswordResetService.RequestedEmails);
        Assert.Empty(factory.PasswordResetService.ResetCalls);
    }

    [Theory]
    [InlineData("/api/v1/auth/password-reset-requests")]
    [InlineData("/api/v1/auth/password-resets")]
    public async Task ExecuteAsync_WhenBodyExceedsMaximumSize_ReturnsPayloadTooLarge(string requestUri)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            requestUri)
        {
            Content = new StringContent(
                "{\"value\":\"" + new string(
                    'a',
                    5 * 1024) + "\"}",
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
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Empty(factory.PasswordResetService.RequestedEmails);
        Assert.Empty(factory.PasswordResetService.ResetCalls);
    }

    [Fact]
    public async Task RequestAsync_WhenSixthRequestWithinOneMinute_IsRateLimited()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        for (var requestNumber = 0; requestNumber < 5; requestNumber++)
        {
            using var accepted = await RequestResetAsync(
                client,
                "member@example.fr");
            Assert.Equal(
                HttpStatusCode.Accepted,
                accepted.StatusCode);
        }

        // Act
        using var response = await RequestResetAsync(
            client,
            "member@example.fr");

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            5,
            factory.PasswordResetService.RequestedEmails.Count);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestRateLimitExceeded,
            error.ErrorCode);
    }

    [Fact]
    public async Task ResetAsync_WhenEleventhAttemptWithinOneMinute_IsRateLimited()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        for (var requestNumber = 0; requestNumber < 10; requestNumber++)
        {
            using var accepted = await ResetAsync(
                client,
                _validUserId,
                ValidToken,
                ValidPassword);
            Assert.Equal(
                HttpStatusCode.NoContent,
                accepted.StatusCode);
        }

        // Act
        using var response = await ResetAsync(
            client,
            _validUserId,
            ValidToken,
            ValidPassword);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            10,
            factory.PasswordResetService.ResetCalls.Count);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestRateLimitExceeded,
            error.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenValuesAreSensitive_DoesNotWriteThemToLogs()
    {
        // Arrange
        const string SensitiveEmail = "sensitive-reset@example.fr";
        const string SensitiveToken = "c2Vuc2l0aXZlLXJlc2V0LXRva2Vu";
        const string SensitivePassword = "sensitive new password";
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        using var requestResponse = await RequestResetAsync(
            client,
            SensitiveEmail);

        // Act
        using var resetResponse = await ResetAsync(
            client,
            _validUserId,
            SensitiveToken,
            SensitivePassword);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            requestResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            resetResponse.StatusCode);
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                SensitiveEmail,
                StringComparison.Ordinal) ||
            message.Contains(
                SensitiveToken,
                StringComparison.Ordinal) ||
            message.Contains(
                SensitivePassword,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOpenApiIsGenerated_DocumentsAnonymousSecureContracts()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response is empty.");
        var paths = document.RootElement.GetProperty("paths");
        var requestOperation = paths
            .GetProperty("/api/v1/auth/password-reset-requests")
            .GetProperty("post");
        var resetOperation = paths
            .GetProperty("/api/v1/auth/password-resets")
            .GetProperty("post");

        // Assert
        Assert.Equal(
            "Requests an account password reset email.",
            requestOperation.GetProperty("summary").GetString());
        Assert.Equal(
            "Resets an account password using a password reset link.",
            resetOperation.GetProperty("summary").GetString());
        Assert.All(
            [
                requestOperation,
                resetOperation
            ],
            operation =>
            {
                Assert.False(operation.TryGetProperty(
                    "security",
                    out var security) && security.GetArrayLength() > 0);
                Assert.Contains(
                    operation.GetProperty("parameters").EnumerateArray(),
                    parameter => parameter.GetProperty("name").GetString() ==
                        WebSecurityOptions.AntiforgeryHeaderName);
            });
        Assert.Equal(
            ["email"],
            GetRequestPropertyNames(
                document.RootElement,
                requestOperation));
        Assert.Equal(
            [
                "newPassword",
                "token",
                "userId"
            ],
            GetRequestPropertyNames(
                document.RootElement,
                resetOperation));
        var requestResponses = requestOperation.GetProperty("responses");
        var resetResponses = resetOperation.GetProperty("responses");
        Assert.False(requestResponses.TryGetProperty(
            "401",
            out _));
        Assert.False(requestResponses.TryGetProperty(
            "403",
            out _));
        Assert.False(resetResponses.TryGetProperty(
            "401",
            out _));
        Assert.False(resetResponses.TryGetProperty(
            "403",
            out _));

        foreach (var statusCode in new[]
        {
            "202",
            "400",
            "413",
            "415",
            "429",
            "500",
            "503"
        })
        {
            Assert.True(requestResponses.TryGetProperty(
                statusCode,
                out _));
        }

        foreach (var statusCode in new[]
        {
            "204",
            "400",
            "413",
            "415",
            "429",
            "500",
            "503"
        })
        {
            Assert.True(resetResponses.TryGetProperty(
                statusCode,
                out _));
        }

        Assert.Contains(
            "no-store",
            requestResponses
                .GetProperty("202")
                .GetProperty("headers")
                .GetProperty("Cache-Control")
                .GetProperty("description")
                .GetString(),
            StringComparison.Ordinal);
        var resetHeaders = resetResponses
            .GetProperty("204")
            .GetProperty("headers");
        Assert.Contains(
            "Deletes",
            resetHeaders
                .GetProperty("Set-Cookie")
                .GetProperty("description")
                .GetString(),
            StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> RequestResetAsync(
        HttpClient client,
        string? email)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/password-reset-requests")
        {
            Content = JsonContent.Create(new { email })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> ResetAsync(
        HttpClient client,
        string? userId,
        string? token,
        string? newPassword)
    {
        using var request = await CreateResetRequestAsync(
            client,
            userId,
            token,
            newPassword);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> ResetWithCookieAsync(
        HttpClient client,
        string? userId,
        string? token,
        string? newPassword)
    {
        using var request = await CreateResetRequestAsync(
            client,
            userId,
            token,
            newPassword);
        request.Headers.Add(
            "Cookie",
            "MonKado.Refresh=current-refresh-token");

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpRequestMessage> CreateResetRequestAsync(
        HttpClient client,
        string? userId,
        string? token,
        string? newPassword)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/password-resets")
        {
            Content = JsonContent.Create(new
            {
                userId,
                token,
                newPassword
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return request;
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

    private static async Task AssertInvalidResetAsync(HttpResponseMessage response)
    {
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountPasswordResetInvalid,
            error.ErrorCode);
        Assert.Equal(
            "The password reset link is invalid or expired.",
            error.Message);
        Assert.Null(error.ValidationErrors);
    }

    private static string[] GetRequestPropertyNames(
        JsonElement document,
        JsonElement operation)
    {
        var schema = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        var reference = schema.GetProperty("$ref").GetString()
            ?? throw new InvalidOperationException("The schema reference is invalid.");
        var schemaName = reference.Split('/').Last();

        return document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName)
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(property => property)
            .ToArray();
    }
}
