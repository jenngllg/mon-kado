using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class LogoutTests
{
    private const string Audience = "MonKado.Frontend";
    private const string Issuer = "MonKado.Api";
    private const string LocalRefreshCookieName = "MonKado.Refresh";
    private const string ProductionRefreshCookieName = "__Host-MonKado.Refresh";
    private const string RefreshToken = "functional-refresh-token";
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogoutAsync_WhenBearerIsAbsentOrExpired_ClearsCurrentBrowserSession(
        bool includeExpiredBearer)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);

        if (includeExpiredBearer)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                CreateExpiredAccessToken());

        // Act
        using var response = await LogoutAsync(
            client,
            csrfToken,
            LocalRefreshCookieName,
            RefreshToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(
            TestContext.Current.CancellationToken));
        var deletedCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                $"{LocalRefreshCookieName}=;",
                StringComparison.Ordinal));
        Assert.Contains(
            "httponly",
            deletedCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "samesite=strict",
            deletedCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [RefreshToken],
            factory.SessionService.LogoutRefreshTokens);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                "Current browser session logout completed.",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                RefreshToken,
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task LogoutAsync_WhenCookieIsMissingOrMalformed_ReturnsNoContentWithoutPostgreSql(
        string? refreshToken)
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);

        // Act
        using var response = await LogoutAsync(
            client,
            csrfToken,
            LocalRefreshCookieName,
            refreshToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                $"{LocalRefreshCookieName}=;",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogoutAsync_WhenCsrfTokenIsMissing_ReturnsBadRequestWithoutCallingService()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.DeleteAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.SessionService.LogoutRefreshTokens);
    }

    [Fact]
    public async Task LogoutAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailableAndPreservesCookie()
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        var refreshToken = $"{Guid.CreateVersion7():N}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        // Act
        using var response = await LogoutAsync(
            client,
            csrfToken,
            LocalRefreshCookieName,
            refreshToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.TryGetValues(
            "Set-Cookie",
            out var cookies) && cookies.Any(value => value.StartsWith(
                $"{LocalRefreshCookieName}=;",
                StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LogoutAsync_WhenProduction_DeletesSecureHostCookie()
    {
        // Arrange
        using var keys = new TemporaryKeyDirectory();
        await using var factory = new RegistrationApiFactory(
            "Production",
            keys.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var csrfToken = await GetCsrfTokenAsync(client);

        // Act
        using var response = await LogoutAsync(
            client,
            csrfToken,
            ProductionRefreshCookieName,
            RefreshToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        var deletedCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                $"{ProductionRefreshCookieName}=;",
                StringComparison.Ordinal));
        Assert.Contains(
            "secure",
            deletedCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "domain=",
            deletedCookie,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LogoutAsync_WhenEleventhRequestWithinOneMinute_IsRateLimited()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using var accepted = await LogoutAsync(
                client,
                csrfToken,
                LocalRefreshCookieName,
                RefreshToken);
            Assert.Equal(
                HttpStatusCode.NoContent,
                accepted.StatusCode);
        }

        // Act
        using var rejected = await LogoutAsync(
            client,
            csrfToken,
            LocalRefreshCookieName,
            RefreshToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            10,
            factory.SessionService.LogoutRefreshTokens.Count);
    }

    private static string CreateExpiredAccessToken()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            [
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    Guid.CreateVersion7().ToString("D"))
            ],
            notBefore: null,
            expires: DateTime.UtcNow.AddMinutes(-5),
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<HttpResponseMessage> LogoutAsync(
        HttpClient client,
        string csrfToken,
        string cookieName,
        string? refreshToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/v1/auth/sessions/current");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        if (refreshToken is not null)
            request.Headers.Add(
                "Cookie",
                $"{cookieName}={refreshToken}");

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
