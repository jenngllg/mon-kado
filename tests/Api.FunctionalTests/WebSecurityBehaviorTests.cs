using System.Net;
using System.Net.Http.Json;
using JennGllg.Fr.MonKado.Back.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public sealed class WebSecurityBehaviorTests
{
    private const string AllowedOrigin = "http://localhost:5173";

    [Fact]
    public async Task AllowedPreflightReturnsExactCredentialedCorsPolicy()
    {
        using SecurityApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Options, "/_tests/security/mutate");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        Assert.Contains("POST", JoinHeader(response, "Access-Control-Allow-Methods"), StringComparison.Ordinal);
        Assert.Contains("x-csrf-token", JoinHeader(response, "Access-Control-Allow-Headers"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("600", Assert.Single(response.Headers.GetValues("Access-Control-Max-Age")));
    }

    [Fact]
    public async Task UnknownOriginReceivesNoCorsAuthorizationHeaders()
    {
        using SecurityApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/liveness");
        request.Headers.Add("Origin", "https://malicious.example");

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task CsrfEndpointReturnsTokenNoStoreAndHardenedSessionCookie()
    {
        using SecurityApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        CsrfTokenResponse? payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken);
        string cookie = GetAntiforgeryCookie(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(payload?.Token));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.StartsWith("MonKado.Antiforgery=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MutatingControllerRejectsMissingAndInvalidTokens()
    {
        using SecurityApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage missingResponse = await client.PostAsync(
            "/_tests/security/mutate",
            JsonContent.Create(new { }),
            TestContext.Current.CancellationToken);

        using HttpResponseMessage tokenResponse = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        using HttpRequestMessage invalidRequest = new(HttpMethod.Post, "/_tests/security/mutate")
        {
            Content = JsonContent.Create(new { })
        };
        invalidRequest.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, "invalid-token");
        using HttpResponseMessage invalidResponse = await client.SendAsync(
            invalidRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task MutatingControllerAcceptsValidCookieAndHeaderToken()
    {
        using SecurityApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string token = await GetCsrfToken(client);
        using HttpRequestMessage request = new(HttpMethod.Post, "/_tests/security/mutate")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, token);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SafeEndpointsRemainAvailableAndIncludeSecurityHeaders()
    {
        using SecurityApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage safeResponse = await client.GetAsync(
            "/_tests/security/safe",
            TestContext.Current.CancellationToken);
        using HttpResponseMessage livenessResponse = await client.GetAsync(
            "/liveness",
            TestContext.Current.CancellationToken);
        using HttpResponseMessage openApiResponse = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, safeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, livenessResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal(
            "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
            Assert.Single(livenessResponse.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("DENY", Assert.Single(livenessResponse.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("nosniff", Assert.Single(livenessResponse.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(livenessResponse.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "camera=(), microphone=(), geolocation=()",
            Assert.Single(livenessResponse.Headers.GetValues("Permissions-Policy")));
        Assert.Equal("same-site", Assert.Single(livenessResponse.Headers.GetValues("Cross-Origin-Resource-Policy")));
        Assert.False(livenessResponse.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task ProductionHttpsUsesHostCookieAndHsts()
    {
        using TemporaryKeyDirectory keys = new();
        using SecurityApiFactory factory = new(
            environment: "Production",
            allowedOrigin: "https://app.example.test",
            allowedHosts: "api.example.test",
            dataProtectionKeysPath: keys.Path);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://api.example.test")
        });

        using HttpResponseMessage response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        string cookie = GetAntiforgeryCookie(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("__Host-MonKado.Antiforgery=", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task InstancesSharingDataProtectionKeysAcceptTheSameAntiforgeryToken()
    {
        using TemporaryKeyDirectory keys = new();
        using SecurityApiFactory firstFactory = new(dataProtectionKeysPath: keys.Path);
        using HttpClient firstClient = firstFactory.CreateClient();
        using HttpResponseMessage tokenResponse = await firstClient.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        CsrfTokenResponse payload = await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
        string cookie = GetAntiforgeryCookie(tokenResponse).Split(';', 2)[0];

        using SecurityApiFactory secondFactory = new(dataProtectionKeysPath: keys.Path);
        using HttpClient secondClient = secondFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        using HttpRequestMessage request = new(HttpMethod.Post, "/_tests/security/mutate")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, payload.Token);

        using HttpResponseMessage response = await secondClient.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<string> GetCsrfToken(HttpClient client)
    {
        CsrfTokenResponse? payload = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);

        return payload?.Token ?? throw new InvalidOperationException("The CSRF token response is empty.");
    }

    private static string GetAntiforgeryCookie(HttpResponseMessage response)
    {
        return Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("MonKado.Antiforgery=", StringComparison.Ordinal) ||
            value.StartsWith("__Host-MonKado.Antiforgery=", StringComparison.Ordinal));
    }

    private static string JoinHeader(HttpResponseMessage response, string name)
    {
        return string.Join(',', response.Headers.GetValues(name));
    }
}
