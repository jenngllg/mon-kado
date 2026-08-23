using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class MemberPasswordChangeTests
{
    public static IEnumerable<object[]> ServiceFailures =>
    [
        [
            new CurrentPasswordInvalidException(),
            HttpStatusCode.Forbidden,
            ErrorCodes.MemberCurrentPasswordInvalid
        ],
        [
            new DependencyUnavailableException(
                "PostgreSQL",
                new TimeoutException()),
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.TechnicalDependencyUnavailable
        ]
    ];

    [Theory]
    [InlineData("Local", "MonKado.Refresh=;")]
    [InlineData("Production", "__Host-MonKado.Refresh=;")]
    public async Task UpdatePasswordAsync_WhenRequestIsValid_ReturnsNoContentAndDeletesCookie(
        string environment,
        string expectedDeletedCookie)
    {
        // Arrange
        using var keys = new TemporaryKeyDirectory();
        await using var factory = new RegistrationApiFactory(
            environment,
            keys.Path);
        var memberId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            memberId);
        using var request = CreateRequest(
            " current password ",
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
        Assert.Equal(
            0,
            response.Content.Headers.ContentLength);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                expectedDeletedCookie,
                StringComparison.Ordinal));
        Assert.Equal(
            [(
                memberId,
                " current password ",
                " new secure password ")],
            factory.MemberPasswordService.Changes);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Password changed for member {memberId}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                " current password ",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                " new secure password ",
                StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ServiceFailures))]
    public async Task UpdatePasswordAsync_WhenServiceRejectsRequest_ReturnsStructuredErrorWithoutDeletingCookie(
        Exception exception,
        HttpStatusCode expectedStatusCode,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.MemberPasswordService.Exception = exception;
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest(
            "current password",
            "new secure password");
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
    public async Task UpdatePasswordAsync_WhenMemberDoesNotExist_ReturnsUnauthorizedAndDeletesCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.MemberPasswordService.Result = false;
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest(
            "current password",
            "new secure password");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "new secure password")]
    [InlineData("", "new secure password")]
    [InlineData("   ", "new secure password")]
    [InlineData("current password", null)]
    [InlineData("current password", "   ")]
    [InlineData("current password", "short")]
    [InlineData("same password", "same password")]
    public async Task UpdatePasswordAsync_WhenBodyIsInvalid_ReturnsValidationErrorWithoutDeletingCookie(
        string? currentPassword,
        string? newPassword)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest(
            currentPassword,
            newPassword);
        request.Headers.Add(
            "Cookie",
            "MonKado.Refresh=current-refresh-token");

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
        Assert.NotNull(error.ValidationErrors);
        Assert.Empty(factory.MemberPasswordService.Changes);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenBearerIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "current password",
            "new secure password");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.MemberPasswordService.Changes);
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenContentTypeIsNotJson_ReturnsUnsupportedMediaType()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/password")
        {
            Content = new StringContent(
                "currentPassword=value",
                Encoding.UTF8,
                "text/plain")
        };

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
        Assert.Empty(factory.MemberPasswordService.Changes);
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenBodyExceedsMaximumSize_ReturnsPayloadTooLarge()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/password")
        {
            Content = new StringContent(
                "{\"currentPassword\":\"" + new string(
                    'a',
                    5 * 1024) + "\",\"newPassword\":\"new secure password\"}",
                Encoding.UTF8,
                "application/json")
        };

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
        Assert.Empty(factory.MemberPasswordService.Changes);
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenRateLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        client.DefaultRequestHeaders.Add(
            "Cookie",
            "MonKado.Refresh=current-refresh-token");

        // Act
        for (var requestNumber = 0; requestNumber < 5; requestNumber++)
        {
            using var accepted = await SendValidRequestAsync(client);
            Assert.Equal(
                HttpStatusCode.NoContent,
                accepted.StatusCode);
        }

        using var rejected = await SendValidRequestAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            5,
            factory.MemberPasswordService.Changes.Count);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
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

    private static HttpRequestMessage CreateRequest(
        string? currentPassword,
        string? newPassword)
    {

        return new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword,
                newPassword
            })
        };
    }

    private static async Task<HttpResponseMessage> SendValidRequestAsync(HttpClient client)
    {
        using var request = CreateRequest(
            "current password",
            "new secure password");

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }
}
