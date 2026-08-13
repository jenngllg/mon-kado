using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JennGllg.Fr.MonKado.Back.Api.Security;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public sealed class EmailConfirmationTests
{
    private static readonly string ValidUserId = Guid.CreateVersion7().ToString("D");
    private const string ValidToken = "dG9rZW4";

    [Fact]
    public async Task ValidConfirmationReturnsEmptyNoContentWithoutSessionCookie()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Confirm(client, ValidUserId, ValidToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(0, response.Content.Headers.ContentLength);
        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            Assert.DoesNotContain(cookies, cookie =>
                cookie.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        }
        EmailConfirmationCall call = Assert.Single(factory.EmailConfirmationService.ConfirmationCalls);
        Assert.Equal(ValidUserId, call.UserId);
        Assert.Equal(ValidToken, call.Token);
    }

    [Fact]
    public async Task InvalidLinkReturnsGenericProblemWithoutCallingInfrastructure()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Confirm(client, "not-a-guid", "invalid token");

        await AssertInvalidConfirmationProblem(response);
        Assert.Equal(0, factory.EmailConfirmationService.ConfirmCallCount);
    }

    [Fact]
    public async Task RejectedTokenReturnsTheSameGenericProblem()
    {
        await using RegistrationApiFactory factory = new();
        factory.EmailConfirmationService.ConfirmationResult = false;
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Confirm(client, ValidUserId, ValidToken);

        await AssertInvalidConfirmationProblem(response);
    }

    [Fact]
    public async Task ConfirmationRequiresCsrfToken()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/email-confirmations",
            new { userId = ValidUserId, token = ValidToken },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.EmailConfirmationService.ConfirmCallCount);
    }

    [Fact]
    public async Task EleventhConfirmationWithinOneMinuteIsRateLimited()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        for (int requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using HttpResponseMessage accepted = await Confirm(client, ValidUserId, ValidToken);
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }

        using HttpResponseMessage rejected = await Confirm(client, ValidUserId, ValidToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(10, factory.EmailConfirmationService.ConfirmCallCount);
    }

    [Fact]
    public async Task ValidResendReturnsGenericAcceptedResponseAndTrimsEmail()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await RequestConfirmation(client, " Lea@example.fr ");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(0, response.Content.Headers.ContentLength);
        Assert.Equal("Lea@example.fr", Assert.Single(factory.EmailConfirmationService.RequestedEmails));
    }

    [Fact]
    public async Task InvalidResendEmailReturnsValidationProblem()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await RequestConfirmation(client, "invalid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The validation response is empty.");
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, factory.EmailConfirmationService.RequestCallCount);
    }

    [Fact]
    public async Task SixthResendWithinOneMinuteIsRateLimited()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        for (int requestNumber = 1; requestNumber <= 5; requestNumber++)
        {
            using HttpResponseMessage accepted = await RequestConfirmation(client, "lea@example.fr");
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        using HttpResponseMessage rejected = await RequestConfirmation(client, "lea@example.fr");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(5, factory.EmailConfirmationService.RequestCallCount);
    }

    [Theory]
    [InlineData("/api/v1/auth/email-confirmations")]
    [InlineData("/api/v1/auth/email-confirmation-requests")]
    public async Task NonJsonContentReturnsUnsupportedMediaType(string requestUri)
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, requestUri)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, factory.EmailConfirmationService.ConfirmCallCount);
        Assert.Equal(0, factory.EmailConfirmationService.RequestCallCount);
    }

    [Theory]
    [InlineData("/api/v1/auth/email-confirmations")]
    [InlineData("/api/v1/auth/email-confirmation-requests")]
    public async Task RequestBodyLargerThanFourKibibytesIsRejected(string requestUri)
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string csrfToken = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(
                "{\"value\":\"" + new string('a', 5 * 1024) + "\"}",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, factory.EmailConfirmationService.ConfirmCallCount);
        Assert.Equal(0, factory.EmailConfirmationService.RequestCallCount);
    }

    [Fact]
    public async Task ConfirmationAndResendValuesAreNeverWrittenToLogs()
    {
        const string SensitiveToken = "c2Vuc2l0aXZlLXRva2Vu";
        const string SensitiveEmail = "sensitive-confirmation@example.fr";
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage confirmation = await Confirm(client, ValidUserId, SensitiveToken);
        using HttpResponseMessage resend = await RequestConfirmation(client, SensitiveEmail);

        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.DoesNotContain(factory.LogMessages, message =>
            message.Contains(ValidUserId, StringComparison.Ordinal) ||
            message.Contains(SensitiveToken, StringComparison.Ordinal) ||
            message.Contains(SensitiveEmail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnavailablePostgreSqlReturnsServiceUnavailableForBothEndpoints()
    {
        await using UnavailablePostgreSqlApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage confirmation = await Confirm(client, ValidUserId, ValidToken);
        using HttpResponseMessage resend = await RequestConfirmation(client, "lea@example.fr");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, confirmation.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resend.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocumentsBodyOnlyTokensAndCsrfHeaders()
    {
        await using RegistrationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using JsonDocument document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response is empty.");

        JsonElement paths = document.RootElement.GetProperty("paths");
        JsonElement confirmation = paths.GetProperty("/api/v1/auth/email-confirmations").GetProperty("post");
        JsonElement resend = paths.GetProperty("/api/v1/auth/email-confirmation-requests").GetProperty("post");

        Assert.True(confirmation.GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.True(resend.GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.DoesNotContain(
            confirmation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("in").GetString() == "query");
        Assert.All(
            new[] { confirmation, resend },
            operation => Assert.Contains(
                operation.GetProperty("parameters").EnumerateArray(),
                parameter => parameter.GetProperty("name").GetString() ==
                    WebSecurityOptions.AntiforgeryHeaderName));
    }

    private static async Task<HttpResponseMessage> Confirm(
        HttpClient client,
        string userId,
        string token)
    {
        string csrfToken = await GetCsrfToken(client);
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/email-confirmations")
        {
            Content = JsonContent.Create(new { userId, token })
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> RequestConfirmation(HttpClient client, string email)
    {
        string csrfToken = await GetCsrfToken(client);
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/email-confirmation-requests")
        {
            Content = JsonContent.Create(new { email })
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

    private static async Task AssertInvalidConfirmationProblem(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The confirmation problem is empty.");
        JsonElement root = document.RootElement;
        Assert.Equal("EMAIL_CONFIRMATION_INVALID", root.GetProperty("code").GetString());
        Assert.Equal("The email confirmation link is invalid or expired.", root.GetProperty("detail").GetString());
        Assert.False(root.TryGetProperty("errors", out _));
    }
}
