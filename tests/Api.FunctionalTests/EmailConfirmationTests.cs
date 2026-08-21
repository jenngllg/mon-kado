using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class EmailConfirmationTests
{
    private static readonly string _validUserId = Guid.CreateVersion7().ToString("D");
    private const string ValidToken = "dG9rZW4";

    [Fact]
    public async Task ExecuteAsync_WhenValidConfirmation_ReturnsEmptyNoContentWithoutSessionCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await ConfirmAsync(
            client,
            _validUserId,
            ValidToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            0,
            response.Content.Headers.ContentLength);

        if (response.Headers.TryGetValues(
            "Set-Cookie",
            out var cookies))
        {
            Assert.DoesNotContain(
                cookies,
                cookie =>
                cookie.Contains(
                    "Identity",
                    StringComparison.OrdinalIgnoreCase));
        }
        var call = Assert.Single(factory.EmailConfirmationService.ConfirmationCalls);
        Assert.Equal(
            _validUserId,
            call.UserId);
        Assert.Equal(
            ValidToken,
            call.Token);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvalidLink_ReturnsGenericProblemWithoutCallingInfrastructure()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        using var response = await ConfirmAsync(
            client,
            "not-a-guid",
            "invalid token");

        // Act
        await AssertInvalidConfirmationProblemAsync(response);
        // Assert
        Assert.Equal(
            0,
            factory.EmailConfirmationService.ConfirmCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRejectedToken_ReturnsTheSameGenericProblem()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.EmailConfirmationService.ConfirmationResult = false;
        using var client = factory.CreateClient();

        using var response = await ConfirmAsync(
            client,
            _validUserId,
            ValidToken);

        // Act
        await AssertInvalidConfirmationProblemAsync(response);
        // Assert
    }

    [Fact]
    public async Task ExecuteAsync_WhenConfirmation_RequiresCsrfToken()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/email-confirmations",
            new
            {
                userId = _validUserId,
                token = ValidToken
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            0,
            factory.EmailConfirmationService.ConfirmCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEleventhConfirmationWithinOneMinute_IsRateLimited()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using var accepted = await ConfirmAsync(
                client,
                _validUserId,
                ValidToken);
            Assert.Equal(
                HttpStatusCode.NoContent,
                accepted.StatusCode);
        }

        // Act
        using var rejected = await ConfirmAsync(
            client,
            _validUserId,
            ValidToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            10,
            factory.EmailConfirmationService.ConfirmCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidResend_ReturnsGenericAcceptedResponseAndTrimsEmail()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await RequestConfirmationAsync(
            client,
            " Lea@example.fr ");

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            0,
            response.Content.Headers.ContentLength);
        Assert.Equal(
            "Lea@example.fr",
            Assert.Single(factory.EmailConfirmationService.RequestedEmails));
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvalidResendEmail_ReturnsValidationProblem()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await RequestConfirmationAsync(
            client,
            "invalid");

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The validation response is empty.");
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            0,
            factory.EmailConfirmationService.RequestCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSixthResendWithinOneMinute_IsRateLimited()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        for (var requestNumber = 1; requestNumber <= 5; requestNumber++)
        {
            using var accepted = await RequestConfirmationAsync(
                client,
                "lea@example.fr");
            Assert.Equal(
                HttpStatusCode.Accepted,
                accepted.StatusCode);
        }

        // Act
        using var rejected = await RequestConfirmationAsync(
            client,
            "lea@example.fr");

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            5,
            factory.EmailConfirmationService.RequestCallCount);
    }

    [Theory]
    [InlineData("/api/v1/auth/email-confirmations")]
    [InlineData("/api/v1/auth/email-confirmation-requests")]
    public async Task ExecuteAsync_WhenNonJsonContent_ReturnsUnsupportedMediaType(string requestUri)
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
        Assert.Equal(
            0,
            factory.EmailConfirmationService.ConfirmCallCount);
        Assert.Equal(
            0,
            factory.EmailConfirmationService.RequestCallCount);
    }

    [Theory]
    [InlineData("/api/v1/auth/email-confirmations")]
    [InlineData("/api/v1/auth/email-confirmation-requests")]
    public async Task ExecuteAsync_WhenRequestBodyLargerThanFourKibibytes_IsRejected(string requestUri)
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
        Assert.Equal(
            0,
            factory.EmailConfirmationService.ConfirmCallCount);
        Assert.Equal(
            0,
            factory.EmailConfirmationService.RequestCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConfirmationAndResendValues_AreNeverWrittenToLogs()
    {
        // Arrange
        const string SensitiveToken = "c2Vuc2l0aXZlLXRva2Vu";
        const string SensitiveEmail = "sensitive-confirmation@example.fr";
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        using var confirmation = await ConfirmAsync(
            client,
            _validUserId,
            SensitiveToken);
        // Act
        using var resend = await RequestConfirmationAsync(
            client,
            SensitiveEmail);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmation.StatusCode);
        Assert.Equal(
            HttpStatusCode.Accepted,
            resend.StatusCode);
        Assert.DoesNotContain(
            factory.LogMessages,
            message =>
            message.Contains(
                _validUserId,
                StringComparison.Ordinal) ||
            message.Contains(
                SensitiveToken,
                StringComparison.Ordinal) ||
            message.Contains(
                SensitiveEmail,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WhenUnavailablePostgreSql_ReturnsServiceUnavailableForBothEndpoints()
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        using var client = factory.CreateClient();

        using var confirmation = await ConfirmAsync(
            client,
            _validUserId,
            ValidToken);
        // Act
        using var resend = await RequestConfirmationAsync(
            client,
            "lea@example.fr");

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            confirmation.StatusCode);
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            resend.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOpenApi_DocumentsBodyOnlyTokensAndCsrfHeaders()
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
        var confirmation = paths.GetProperty("/api/v1/auth/email-confirmations").GetProperty("post");
        var resend = paths.GetProperty("/api/v1/auth/email-confirmation-requests").GetProperty("post");

        // Assert
        Assert.True(confirmation.GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.True(resend.GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.DoesNotContain(
            confirmation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("in").GetString() == "query");
        Assert.All(
            [
                confirmation,
                resend
            ],
            operation => Assert.Contains(
                operation.GetProperty("parameters").EnumerateArray(),
                parameter => parameter.GetProperty("name").GetString() ==
                    WebSecurityOptions.AntiforgeryHeaderName));
    }

    private static async Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client,
        string userId,
        string token)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/email-confirmations")
        {
            Content = JsonContent.Create(new
            {
                userId,
                token
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> RequestConfirmationAsync(
        HttpClient client,
        string email)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/email-confirmation-requests")
        {
            Content = JsonContent.Create(new
            {
                email
            })
        };
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

    private static async Task AssertInvalidConfirmationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The confirmation problem is empty.");
        var root = document.RootElement;
        Assert.Equal(
            ErrorCodes.AccountEmailConfirmationInvalid,
            root.GetProperty("errorCode").GetString());
        Assert.Equal(
            "The email confirmation link is invalid or expired.",
            root.GetProperty("message").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("validationErrors").ValueKind);
    }
}
