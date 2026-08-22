using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WebSecurityBehaviorTests
{
    private const string AllowedOrigin = "http://localhost:5173";

    [Fact]
    public async Task ExecuteAsync_WhenAllowedPreflight_ReturnsExactCredentialedCorsPolicy()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/_tests/security/mutate");
        request.Headers.Add(
            "Origin",
            AllowedOrigin);
        request.Headers.Add(
            "Access-Control-Request-Method",
            "POST");
        request.Headers.Add(
            "Access-Control-Request-Headers",
            "authorization,content-type,x-csrf-token");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.Equal(
            AllowedOrigin,
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(
            "true",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        Assert.Contains(
            "POST",
            JoinHeader(
                response,
                "Access-Control-Allow-Methods"),
            StringComparison.Ordinal);
        Assert.Contains(
            "x-csrf-token",
            JoinHeader(
                response,
                "Access-Control-Allow-Headers"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "authorization",
            JoinHeader(
                response,
                "Access-Control-Allow-Headers"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "600",
            Assert.Single(response.Headers.GetValues("Access-Control-Max-Age")));
    }

    [Fact]
    public async Task ExecuteAsync_WhenUnknownOriginReceivesNoCorsAuthorizationHeaders_Completes()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/liveness");
        request.Headers.Add(
            "Origin",
            "https://malicious.example");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCsrfEndpoint_ReturnsTokenNoStoreAndHardenedSessionCookie()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        // Act
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken);
        var cookie = GetAntiforgeryCookie(response);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(payload?.Token));
        Assert.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.StartsWith(
            "MonKado.Antiforgery=",
            cookie,
            StringComparison.Ordinal);
        Assert.Contains(
            "path=/",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "httponly",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "samesite=lax",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "domain=",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "expires=",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "max-age=",
            cookie,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMutatingController_RejectsMissingAndInvalidTokens()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();

        using var missingResponse = await client.PostAsync(
            "/_tests/security/mutate",
            JsonContent.Create(new
            {
            }),
            TestContext.Current.CancellationToken);

        using var tokenResponse = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        using var invalidRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/_tests/security/mutate")
        {
            Content = JsonContent.Create(new
            {
            })
        };
        invalidRequest.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            "invalid-token");
        // Act
        using var invalidResponse = await client.SendAsync(
            invalidRequest,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            missingResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            invalidResponse.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMutatingController_AcceptsValidCookieAndHeaderToken()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/_tests/security/mutate")
        {
            Content = JsonContent.Create(new
            {
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            token);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBearerTokenIsValid_AuthorizesWithoutAntiforgeryToken()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var token = accessTokenService.Create(Guid.NewGuid());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            token.Value);

        // Act
        using var response = await client.GetAsync(
            "/_tests/security/bearer",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    public async Task ExecuteAsync_WhenBearerTokenIsInvalid_ReturnsStructuredUnauthorized(string scenario)
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        var token = CreateInvalidToken(scenario);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            token);

        // Act
        using var response = await client.GetAsync(
            "/_tests/security/bearer",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            error.StatusCode);
        Assert.Equal(
            ErrorCodes.SecurityUnauthorized,
            error.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSafeEndpointsRemainAvailableAndIncludeSecurityHeaders_Completes()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();

        using var safeResponse = await client.GetAsync(
            "/_tests/security/safe",
            TestContext.Current.CancellationToken);
        using var livenessResponse = await client.GetAsync(
            "/liveness",
            TestContext.Current.CancellationToken);
        // Act
        using var openApiResponse = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            safeResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            livenessResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            openApiResponse.StatusCode);
        Assert.Equal(
            "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
            Assert.Single(livenessResponse.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal(
            "DENY",
            Assert.Single(livenessResponse.Headers.GetValues("X-Frame-Options")));
        Assert.Equal(
            "nosniff",
            Assert.Single(livenessResponse.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(
            "no-referrer",
            Assert.Single(livenessResponse.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "camera=(), microphone=(), geolocation=()",
            Assert.Single(livenessResponse.Headers.GetValues("Permissions-Policy")));
        Assert.Equal(
            "same-site",
            Assert.Single(livenessResponse.Headers.GetValues("Cross-Origin-Resource-Policy")));
        Assert.False(livenessResponse.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenUnavailableDependencyHasNoInnerException_ReturnsServiceUnavailable()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/_tests/security/unavailable",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenModelBindingFails_ReturnsNormalizedValidationErrors()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();

        using var invalidQueryResponse = await client.GetAsync(
            "/_tests/security/invalid-query?value=invalid",
            TestContext.Current.CancellationToken);
        using var emptyErrorResponse = await client.GetAsync(
            "/_tests/security/empty-binding-error?value=invalid",
            TestContext.Current.CancellationToken);

        // Act
        using var missingBodyResponse = await client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Post,
                "/_tests/security/required-body")
            {
                Content = new StringContent(
                    string.Empty,
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            invalidQueryResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            emptyErrorResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            missingBodyResponse.StatusCode);
        var invalidQuery = await invalidQueryResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        var missingBody = await missingBodyResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        var emptyError = await emptyErrorResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "value",
            Assert.Single(invalidQuery?.ValidationErrors ?? []).PropertyName);
        var missingBodyErrors = missingBody?.ValidationErrors?.ToArray() ?? [];
        Assert.NotEmpty(missingBodyErrors);
        Assert.All(
            missingBodyErrors,
            error => Assert.Equal(
                "body",
                error.PropertyName));
        Assert.Equal(
            "The request body is invalid.",
            Assert.Single(emptyError?.ValidationErrors ?? []).ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRemoteIpIsAvailable_PartitionsAuthenticationRequestByAddress()
    {
        // Arrange
        using var factory = new SecurityApiFactory(remoteIpAddress: IPAddress.Loopback);
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions")
        {
            Content = JsonContent.Create(new
            {
                email = "member@example.fr",
                password = "a sufficiently long password",
                rememberMe = false
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
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProductionHttps_UsesHostCookieAndHsts()
    {
        // Arrange
        using var keys = new TemporaryKeyDirectory();
        using var factory = new SecurityApiFactory(
            environment: "Production",
            allowedOrigin: "https://app.example.test",
            allowedHosts: "api.example.test",
            dataProtectionKeysPath: keys.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://api.example.test")
        });

        // Act
        using var response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        var cookie = GetAntiforgeryCookie(response);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.StartsWith(
            "__Host-MonKado.Antiforgery=",
            cookie,
            StringComparison.Ordinal);
        Assert.Contains(
            "secure",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "max-age=31536000",
            Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task ExecuteAsync_WhenInstancesSharingDataProtectionKeysAcceptTheSameAntiforgeryToken_Completes()
    {
        // Arrange
        using var keys = new TemporaryKeyDirectory();
        using var firstFactory = new SecurityApiFactory(dataProtectionKeysPath: keys.Path);
        using var firstClient = firstFactory.CreateClient();
        using var tokenResponse = await firstClient.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        var payload = await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
        var cookie = GetAntiforgeryCookie(tokenResponse).Split(
            ';',
            2)[0];

        using var secondFactory = new SecurityApiFactory(dataProtectionKeysPath: keys.Path);
        using var secondClient = secondFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/_tests/security/mutate")
        {
            Content = JsonContent.Create(new
            {
            })
        };
        request.Headers.Add(
            "Cookie",
            cookie);
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            payload.Token);

        // Act
        using var response = await secondClient.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var payload = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);

        return payload?.Token ?? throw new InvalidOperationException("The CSRF token response is empty.");
    }

    private static string GetAntiforgeryCookie(HttpResponseMessage response)
    {

        return Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value =>
            value.StartsWith(
                "MonKado.Antiforgery=",
                StringComparison.Ordinal) ||
            value.StartsWith(
                "__Host-MonKado.Antiforgery=",
                StringComparison.Ordinal));
    }

    private static string JoinHeader(
        HttpResponseMessage response,
        string name)
    {

        return string.Join(
            ',',
            response.Headers.GetValues(name));
    }

    private static string CreateInvalidToken(string scenario)
    {
        var issuer = scenario == "issuer"
            ? "Unexpected.Api"
            : SecurityApiFactory.JwtIssuer;
        var audience = scenario == "audience"
            ? "Unexpected.Frontend"
            : SecurityApiFactory.JwtAudience;
        var signingKey = scenario == "signature"
            ? "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8="
            : SecurityApiFactory.JwtSigningKey;
        var expires = scenario == "expired"
            ? DateTime.UtcNow.AddMinutes(-1)
            : DateTime.UtcNow.AddMinutes(15);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    Guid.NewGuid().ToString("D"))
            ],
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
