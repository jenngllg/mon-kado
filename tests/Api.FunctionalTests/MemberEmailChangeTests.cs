using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class MemberEmailChangeTests
{
    private const string CurrentEntityTag = "\"0000002a\"";

    public static IEnumerable<object[]> RequestFailures =>
    [
        [
            new CurrentPasswordInvalidException(),
            HttpStatusCode.Forbidden,
            ErrorCodes.MemberCurrentPasswordInvalid
        ],
        [
            new MemberEmailAlreadyUsedException(),
            HttpStatusCode.Conflict,
            ErrorCodes.MemberEmailAlreadyUsed
        ],
        [
            new MemberProfileVersionConflictException(),
            HttpStatusCode.PreconditionFailed,
            ErrorCodes.MemberProfileVersionConflict
        ],
        [
            new DependencyUnavailableException(
                "PostgreSQL",
                new TimeoutException()),
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.TechnicalDependencyUnavailable
        ]
    ];

    [Fact]
    public async Task UpdateEmailAsync_WhenRequestIsValid_ReturnsAcceptedWithoutBody()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            memberId);
        using var request = CreateEmailChangeRequest(
            " new@example.fr ",
            "current-password",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            0,
            response.Content.Headers.ContentLength);
        Assert.Equal(
            [(
                memberId,
                "new@example.fr",
                "current-password",
                42u)],
            factory.MemberEmailChangeService.Requests);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Email change requested for member {memberId}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "new@example.fr",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "current-password",
                StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RequestFailures))]
    public async Task UpdateEmailAsync_WhenServiceRejectsRequest_ReturnsStructuredErrorWithoutDeletingCookie(
        Exception exception,
        HttpStatusCode expectedStatusCode,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.MemberEmailChangeService.RequestException = exception;
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateEmailChangeRequest(
            "new@example.fr",
            "current-password",
            CurrentEntityTag);
        request.Headers.Add(
            "Cookie",
            "MonKado.Refresh=current-refresh-token");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatusCode,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            expectedErrorCode,
            error.ErrorCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenMemberDoesNotExist_ReturnsUnauthorizedAndDeletesCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.MemberEmailChangeService.RequestResult = false;
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateEmailChangeRequest(
            "new@example.fr",
            "current-password",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "current-password")]
    [InlineData("invalid", "current-password")]
    [InlineData("new@example.fr", null)]
    public async Task UpdateEmailAsync_WhenBodyIsInvalid_ReturnsValidationError(
        string? email,
        string? currentPassword)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateEmailChangeRequest(
            email,
            currentPassword,
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

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
        Assert.Empty(factory.MemberEmailChangeService.Requests);
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenIfMatchIsMissing_ReturnsPreconditionRequired()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateEmailChangeRequest(
            "new@example.fr",
            "current-password",
            entityTag: null);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            (HttpStatusCode)StatusCodes.Status428PreconditionRequired,
            response.StatusCode);
        Assert.Empty(factory.MemberEmailChangeService.Requests);
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenBearerIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateEmailChangeRequest(
            "new@example.fr",
            "current-password",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.MemberEmailChangeService.Requests);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenRequestIsValid_ReturnsNoContentAndDeletesCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var requestId = Guid.CreateVersion7();
        using var client = factory.CreateClient();

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            requestId,
            "encoded-token");

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            [(
                requestId,
                "encoded-token")],
            factory.MemberEmailChangeService.Confirmations);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenConfirmationIsInvalid_ReturnsGenericErrorWithoutDeletingCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.MemberEmailChangeService.ConfirmationResult = false;
        using var client = factory.CreateClient();

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            Guid.CreateVersion7(),
            "encoded-token");

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.MemberEmailChangeInvalid,
            error.ErrorCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Conflict, ErrorCodes.MemberEmailAlreadyUsed)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, ErrorCodes.TechnicalDependencyUnavailable)]
    public async Task ConfirmEmailChangeAsync_WhenServiceFails_ReturnsStructuredErrorWithoutDeletingCookie(
        bool emailIsUsed,
        HttpStatusCode expectedStatusCode,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.MemberEmailChangeService.ConfirmationException = emailIsUsed
            ? new MemberEmailAlreadyUsedException()
            : new DependencyUnavailableException(
                "PostgreSQL",
                new TimeoutException());
        using var client = factory.CreateClient();

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            Guid.CreateVersion7(),
            "encoded-token");

        // Assert
        Assert.Equal(
            expectedStatusCode,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            expectedErrorCode,
            error.ErrorCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenAntiforgeryTokenIsMissing_ReturnsBadRequest()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/email-change-confirmations",
            new
            {
                requestId = Guid.CreateVersion7(),
                token = "encoded-token"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.MemberEmailChangeService.Confirmations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmailChangeAsync_WhenContentTypeIsNotJson_ReturnsUnsupportedMediaType(
        bool confirmsRequest)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = confirmsRequest
            ? factory.CreateClient()
            : CreateAuthorizedClient(
                factory,
                Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            confirmsRequest
                ? HttpMethod.Post
                : HttpMethod.Put,
            confirmsRequest
                ? "/api/v1/auth/email-change-confirmations"
                : "/api/v1/members/current/email")
        {
            Content = new StringContent(
                "not-json",
                Encoding.UTF8,
                "text/plain")
        };

        if (confirmsRequest)
            request.Headers.Add(
                WebSecurityOptions.AntiforgeryHeaderName,
                await GetCsrfTokenAsync(client));

        if (!confirmsRequest)
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestUnsupportedMediaType,
            error.ErrorCode);
        Assert.Empty(factory.MemberEmailChangeService.Requests);
        Assert.Empty(factory.MemberEmailChangeService.Confirmations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmailChangeAsync_WhenBodyExceedsMaximumSize_ReturnsPayloadTooLarge(
        bool confirmsRequest)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = confirmsRequest
            ? factory.CreateClient()
            : CreateAuthorizedClient(
                factory,
                Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            confirmsRequest
                ? HttpMethod.Post
                : HttpMethod.Put,
            confirmsRequest
                ? "/api/v1/auth/email-change-confirmations"
                : "/api/v1/members/current/email")
        {
            Content = new StringContent(
                "{\"value\":\"" + new string(
                    'a',
                    5 * 1024) + "\"}",
                Encoding.UTF8,
                "application/json")
        };

        if (confirmsRequest)
            request.Headers.Add(
                WebSecurityOptions.AntiforgeryHeaderName,
                await GetCsrfTokenAsync(client));

        if (!confirmsRequest)
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestPayloadTooLarge,
            error.ErrorCode);
        Assert.Empty(factory.MemberEmailChangeService.Requests);
        Assert.Empty(factory.MemberEmailChangeService.Confirmations);
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 10)]
    public async Task EmailChangeAsync_WhenPolicyLimitIsExceeded_ReturnsTooManyRequests(
        bool confirmsRequest,
        int permitLimit)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = confirmsRequest
            ? factory.CreateClient()
            : CreateAuthorizedClient(
                factory,
                Guid.CreateVersion7());

        for (var requestNumber = 1; requestNumber <= permitLimit; requestNumber++)
        {
            using var accepted = confirmsRequest
                ? await ConfirmEmailChangeAsync(
                    client,
                    Guid.CreateVersion7(),
                    "encoded-token")
                : await RequestEmailChangeAsync(client);
            Assert.True(accepted.IsSuccessStatusCode);
        }

        // Act
        using var rejected = confirmsRequest
            ? await ConfirmEmailChangeAsync(
                client,
                Guid.CreateVersion7(),
                "encoded-token")
            : await RequestEmailChangeAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            confirmsRequest
                ? 0
                : permitLimit,
            factory.MemberEmailChangeService.Requests.Count);
        Assert.Equal(
            confirmsRequest
                ? permitLimit
                : 0,
            factory.MemberEmailChangeService.Confirmations.Count);
    }

    private static HttpClient CreateAuthorizedClient(
        RegistrationApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
    }

    private static HttpRequestMessage CreateEmailChangeRequest(
        string? email,
        string? currentPassword,
        string? entityTag)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/email")
        {
            Content = JsonContent.Create(new
            {
                email,
                currentPassword
            })
        };

        if (entityTag is not null)
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);

        return request;
    }

    private static async Task<HttpResponseMessage> ConfirmEmailChangeAsync(
        HttpClient client,
        Guid requestId,
        string token)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/email-change-confirmations")
        {
            Content = JsonContent.Create(new
            {
                requestId,
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

    private static async Task<HttpResponseMessage> RequestEmailChangeAsync(HttpClient client)
    {
        using var request = CreateEmailChangeRequest(
            "new@example.fr",
            "current-password",
            CurrentEntityTag);

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
