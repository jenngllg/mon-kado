using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class GoogleSessionCompletionIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteAsync_WhenValidatedCookieIsSubmitted_ReturnsJwtAndCurrentPostgreSqlMember(bool rememberMe)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var proof = await PrepareProofAsync(
            factory,
            client,
            rememberMe,
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await database.Users.ToListAsync(TestContext.Current.CancellationToken));
        using var request = CreateCompletionRequest(proof);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        Assert.NotNull(json);
        var tokens = json.RootElement.Deserialize<AccessTokenResponse>(JsonSerializerOptions.Web);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.NotNull(tokens);
        Assert.Equal(
            "Bearer",
            tokens.TokenType);
        Assert.Equal(
            900,
            tokens.ExpiresIn);
        Assert.Equal(
            [
                "accessToken",
                "expiresIn",
                "tokenType"
            ],
            json.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Equal(
            [
                "aud",
                "exp",
                "iat",
                "iss",
                "jti",
                "sub"
            ],
            jwt.Claims
                .Select(claim => claim.Type)
                .OrderBy(type => type));
        var refreshCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                "MonKado.Refresh=",
                StringComparison.Ordinal));
        Assert.Equal(
            rememberMe,
            refreshCookie.Contains(
                "expires=",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "httponly",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "samesite=strict",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokens.AccessToken);
        using var current = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        var member = await current.Content.ReadFromJsonAsync<CurrentSessionResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            current.StatusCode);
        Assert.NotNull(current.Headers.ETag);
        Assert.False(current.Headers.ETag.IsWeak);
        Assert.NotNull(member);
        Assert.Equal(
            jwt.Subject,
            member.Id.ToString("D"));
        Assert.Equal(
            "new-google-member@gmail.com",
            member.Email);
        Assert.Equal(
            "Google member",
            member.DisplayName);
        Assert.Contains(
            "Member",
            member.Roles);
    }

    [Fact]
    public async Task CompleteAsync_WhenSameProofIsSubmittedConcurrently_CreatesOnlyOneSession()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var proof = await PrepareProofAsync(
            factory,
            client,
            false,
            TestContext.Current.CancellationToken);
        using var firstRequest = CreateCompletionRequest(proof);
        using var secondRequest = CreateCompletionRequest(proof);

        // Act
        var responses = await Task.WhenAll(
            client.SendAsync(
                firstRequest,
                TestContext.Current.CancellationToken),
            client.SendAsync(
                secondRequest,
                TestContext.Current.CancellationToken));
        using var first = responses[0];
        using var second = responses[1];

        // Assert
        Assert.Equal(
            [
                HttpStatusCode.OK,
                HttpStatusCode.Unauthorized
            ],
            responses
                .Select(response => response.StatusCode)
                .OrderBy(status => status));
        var rejected = Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.Unauthorized);
        var error = await rejected.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(
            ErrorCodes.GoogleAuthenticationFailed,
            error?.ErrorCode);
        Assert.DoesNotContain(
            rejected.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                "MonKado.Refresh=",
                StringComparison.Ordinal));
    }

    private PostgreSqlApiFactory CreateFactory()
    {

        return new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureHost: builder =>
            {
                builder.UseSetting(
                    "GoogleAuthentication:Enabled",
                    "true");
                builder.UseSetting(
                    "GoogleAuthentication:ClientId",
                    "integration.apps.googleusercontent.com");
                builder.UseSetting(
                    "GoogleAuthentication:ClientSecret",
                    "integration-client-secret");
                builder.UseSetting(
                    "GoogleAuthentication:FrontendOrigin",
                    "https://app.example.test");
                builder.UseSetting(
                    "WebSecurity:AllowedOrigins:0",
                    "https://app.example.test");
            });
    }

    private static HttpClient CreateClient(PostgreSqlApiFactory factory)
    {

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    // Protocol validation is exercised by the simulated OIDC functional suite; only that boundary is prepared here.
    private static async Task<(string Flow, string Csrf, string Cookies)> PrepareProofAsync(
        PostgreSqlApiFactory factory,
        HttpClient client,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        var externalService = factory.Services.GetRequiredService<IGoogleExternalAuthenticationService>();
        var flow = externalService.CreateFlowBinding();
        var properties = externalService.CreateChallengeProperties(
            GoogleAuthenticationConstants.FrontendReturnPath,
            rememberMe,
            null);
        properties.Items[GoogleAuthenticationConstants.FlowBindingProperty] = flow;
        properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty] = GoogleAuthenticationConstants.NoExpectedMemberValue;
        var identity = new ClaimsIdentity(
            [
                new Claim(
                    "sub",
                    "new-google-member"),
                new Claim(
                    "email",
                    "new-google-member@gmail.com"),
                new Claim(
                    "email_verified",
                    "true"),
                new Claim(
                    "name",
                    "Google member")
            ],
            GoogleAuthenticationSchemes.ExternalCookie);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            properties,
            GoogleAuthenticationSchemes.ExternalCookie);
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(GoogleAuthenticationSchemes.ExternalCookie);
        var cookie = options.TicketDataFormat.Protect(ticket);
        using var csrfResponse = await client.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>(cancellationToken);
        Assert.NotNull(csrf);
        var csrfCookie = Assert
            .Single(csrfResponse.Headers.GetValues("Set-Cookie"))
            .Split(';')[0];

        return (flow, csrf.Token, $"{csrfCookie}; {GoogleAuthenticationConstants.LocalExternalCookieName}={cookie}");
    }

    private static HttpRequestMessage CreateCompletionRequest((string Flow, string Csrf, string Cookies) proof)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/google/completions")
        {
            Content = JsonContent.Create(new { flow = proof.Flow })
        };
        request.Headers.TryAddWithoutValidation(
            WebSecurityOptions.AntiforgeryHeaderName,
            proof.Csrf);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            proof.Cookies);

        return request;
    }
}
