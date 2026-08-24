using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class GoogleAuthenticationControllerTests
{
    private const string AntiforgeryCookieName = "MonKado.Antiforgery";
    private const string ExternalCookieName = GoogleAuthenticationConstants.LocalExternalCookieName;
    private const string RefreshCookieName = "MonKado.Refresh";

    public static IEnumerable<object[]> InvalidProviderDisplayNames()
    {
        yield return
        [
            new string(
                'a',
                81)
        ];
        yield return
        [
            "Invalid\u0001Name"
        ];
    }

    [Fact]
    public async Task ChallengeAsync_WhenRequestIsValid_StartsHardenedCodeFlow()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.RefreshSessionService.ProvenSessionId = Guid.CreateVersion7();
        using var client = factory.CreateGoogleClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            $"{RefreshCookieName}=current-browser-refresh");

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/google?returnPath=%2Fmy-lists&rememberMe=true",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);
        Assert.False(IdentityModelEventSource.ShowPII);
        Assert.False(IdentityModelEventSource.LogCompleteSecurityArtifact);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal(
            "https://provider.example.test/authorize",
            location.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            GoogleAuthenticationApiFactory.ClientId,
            Assert.Single(query["client_id"]));
        Assert.Equal(
            "https://localhost/api/v1/auth/google/callback",
            Assert.Single(query["redirect_uri"]));
        Assert.Equal(
            "code",
            Assert.Single(query["response_type"]));
        Assert.Equal(
            "form_post",
            Assert.Single(query["response_mode"]));
        Assert.Equal(
            "openid email profile",
            Assert.Single(query["scope"]));
        Assert.Equal(
            "S256",
            Assert.Single(query["code_challenge_method"]));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(query["code_challenge"])));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(query["state"])));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(query["nonce"])));
        Assert.Equal(
            "select_account",
            Assert.Single(query["prompt"]));
        Assert.DoesNotContain(
            "client_secret",
            location.Query,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(
            "no-referrer",
            Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(
            cookies,
            cookie => cookie.StartsWith(
                    ".AspNetCore.Correlation.",
                    StringComparison.Ordinal) &&
                HasHardenedCrossSiteAttributes(cookie));
        Assert.Contains(
            cookies,
            cookie => cookie.StartsWith(
                    ".AspNetCore.OpenIdConnect.Nonce.",
                    StringComparison.Ordinal) &&
                HasHardenedCrossSiteAttributes(cookie));
        Assert.Equal(
            1,
            factory.RefreshSessionService.ProveCallCount);
        Assert.Equal(
            "current-browser-refresh",
            factory.RefreshSessionService.LastRefreshToken);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
    }

    [Fact]
    public async Task ChallengeAsync_WhenHostIsNotAllowed_RejectsBeforeProviderOrPersistence()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient(handleCookies: false);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auth/google");
        request.Headers.Host = "attacker.example";

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(
            0,
            factory.RefreshSessionService.ProveCallCount);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
    }

    [Fact]
    public async Task ChallengeAsync_WhenForwardedHostIsUntrusted_KeepsAllowedRequestHostInRedirectUri()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient(handleCookies: false);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auth/google");
        request.Headers.Host = "localhost";
        request.Headers.TryAddWithoutValidation(
            "X-Forwarded-Host",
            "attacker.example");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal(
            "https://localhost/api/v1/auth/google/callback",
            Assert.Single(query["redirect_uri"]));
        Assert.DoesNotContain(
            "attacker.example",
            location.OriginalString,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            factory.RefreshSessionService.ProveCallCount);
    }

    [Fact]
    public async Task ChallengeAsync_WhenCurrentSessionPresenceDiffers_UsesLengthIndistinguishableState()
    {
        // Arrange
        using var anonymousFactory = new GoogleAuthenticationApiFactory();
        using var anonymousClient = anonymousFactory.CreateGoogleClient();
        var currentSessionId = Guid.CreateVersion7();
        using var authenticatedFactory = new GoogleAuthenticationApiFactory();
        authenticatedFactory.RefreshSessionService.ProvenSessionId = currentSessionId;
        using var authenticatedClient = authenticatedFactory.CreateGoogleClient();
        authenticatedClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            $"{RefreshCookieName}=current-browser-refresh");

        // Act
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            anonymousClient,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        var authenticatedProtocol = await StartFlowAsync(
            authenticatedClient,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        var persistentProtocol = await StartFlowAsync(
            anonymousClient,
            rememberMe: true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            state.Length,
            authenticatedProtocol.state.Length);
        Assert.Equal(
            state.Length,
            persistentProtocol.state.Length);
        Assert.Equal(
            2,
            anonymousFactory.RefreshSessionService.ProveCallCount);
        Assert.Equal(
            1,
            authenticatedFactory.RefreshSessionService.ProveCallCount);
    }

    [Fact]
    public async Task ChallengeAsync_WhenReturnPathIsNotAllowlisted_ReturnsBadRequestBeforeSessionProof()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/google?returnPath=%2F%2Fevil.example",
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error?.ErrorCode);
        Assert.Equal(
            0,
            factory.RefreshSessionService.ProveCallCount);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
    }

    [Fact]
    public async Task ChallengeAsync_WhenRememberMeIsNotBoolean_ReturnsBadRequestBeforeSessionProof()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/google?rememberMe=not-a-boolean",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            0,
            factory.RefreshSessionService.ProveCallCount);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
    }

    [Fact]
    public async Task ChallengeAsync_WhenRequestUsesHttp_ReturnsBadRequestWithoutRedirectingToProvider()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = false
        });

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/google",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(
            0,
            factory.RefreshSessionService.ProveCallCount);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
    }

    [Fact]
    public async Task ChallengeAsync_WhenProviderIsDisabled_ReturnsServiceUnavailableWithoutMissingSchemeFailure()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory(isEnabled: false);
        using var client = factory.CreateGoogleClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/google",
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error?.ErrorCode);
        Assert.Equal(
            0,
            factory.RefreshSessionService.ProveCallCount);
    }

    [Fact]
    public async Task Callback_WhenProviderIsDisabled_UsesSafeMvcFallback()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory(isEnabled: false);
        using var client = factory.CreateGoogleClient();
        using var content = new FormUrlEncodedContent([]);

        // Act
        using var response = await client.PostAsync(
            GoogleAuthenticationConstants.CallbackPath,
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            response.Headers.Location?.OriginalString);
        Assert.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CompleteAsync_WhenProviderIsDisabledOrRequestIsInsecure_RejectsBeforeReadingTicket(
        bool isEnabled,
        bool useHttps)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory(isEnabled);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(useHttps
                ? "https://localhost"
                : "http://localhost"),
            HandleCookies = false
        });

        // Act
        using var response = await client.GetAsync(
            GoogleAuthenticationConstants.CompletionPath,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            response.Headers.Location?.OriginalString);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                    "[Error]",
                    StringComparison.Ordinal) &&
                message.Contains(
                    "DisabledOrInsecureRequest",
                    StringComparison.Ordinal));
        Assert.Null(factory.GoogleSessionService.LastCompletionContext);
    }

    [Fact]
    public async Task ChallengeAsync_WhenCurrentSessionProofIsUnavailable_ReturnsServiceUnavailableAndPreservesRefreshCookie()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.RefreshSessionService.IsProofUnavailable = true;
        using var client = factory.CreateGoogleClient(handleCookies: false);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            $"{RefreshCookieName}=existing-refresh-canary");

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/google",
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error?.ErrorCode);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        Assert.Null(response.Headers.Location);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            "existing-refresh-canary",
            factory.RefreshSessionService.LastRefreshToken);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
    }

    [Fact]
    public async Task ChallengeAsync_WhenProviderDiscoveryIsUnavailable_ReturnsServiceUnavailableWithoutChangingRefreshCookie()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory(
            isDiscoveryUnavailable: true);
        using var client = factory.CreateGoogleClient(handleCookies: false);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            $"{RefreshCookieName}=existing-refresh-canary");

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/google",
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error?.ErrorCode);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain(
            response.Headers.TryGetValues(
                "Set-Cookie",
                out var cookies)
                    ? cookies
                    : [],
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=",
                StringComparison.Ordinal));
        Assert.Equal(
            "existing-refresh-canary",
            factory.RefreshSessionService.LastRefreshToken);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
    }

    [Fact]
    public async Task ChallengeAsync_WhenRateLimitIsExceeded_ReturnsStructuredTooManyRequests()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient(handleCookies: false);

        for (var requestIndex = 0; requestIndex < 10; requestIndex++)
        {
            using var accepted = await client.GetAsync(
                "/api/v1/auth/google",
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.Redirect,
                accepted.StatusCode);
        }

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/google",
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestRateLimitExceeded,
            error?.ErrorCode);
        Assert.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            10,
            factory.RefreshSessionService.ProveCallCount);
    }

    [Fact]
    public async Task CompleteAsync_WhenProviderCallbackIsValid_CreatesRefreshSessionAndRejectsReplay()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        var expectedMemberId = Guid.CreateVersion7();
        var currentSessionId = Guid.CreateVersion7();
        factory.GoogleSessionService.ExpectedMemberId = expectedMemberId;
        factory.RefreshSessionService.ProvenSessionId = currentSessionId;
        using var client = factory.CreateGoogleClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            $"{RefreshCookieName}=current-browser-refresh");
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: true,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        factory.Backchannel.ExpectedCodeChallenge = codeChallenge;

        // Act
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var flowBinding = GetFlowBinding(callback.Headers.Location);
        var externalCookie = GetCookiePair(
            callback,
            ExternalCookieName);
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            callback.StatusCode);
        Assert.Equal(
            $"{GoogleAuthenticationConstants.CompletionPath}?flow={flowBinding}",
            callback.Headers.Location?.OriginalString);
        Assert.Contains(
            "no-store",
            callback.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        var externalSetCookie = Assert.Single(
            callback.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=",
                StringComparison.Ordinal));
        Assert.Contains(
            "httponly",
            externalSetCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "secure",
            externalSetCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "samesite=lax",
            externalSetCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "max-age=300",
            externalSetCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "domain=",
            externalSetCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "functional-google-subject",
            externalSetCookie,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "member@gmail.com",
            externalSetCookie,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "google-access-token-not-persisted",
            externalSetCookie,
            StringComparison.Ordinal);
        var cookieValue = Uri.UnescapeDataString(externalCookie.Split(
            '=',
            2)[1]);
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(GoogleAuthenticationSchemes.ExternalCookie);
        var protectedTicket = cookieOptions.TicketDataFormat.Unprotect(cookieValue);
        var ticket = Assert.IsType<Microsoft.AspNetCore.Authentication.AuthenticationTicket>(
            protectedTicket);
        Assert.Equal(
            [
                "email",
                "email_verified",
                "name",
                "sub"
            ],
            [.. ticket.Principal.Claims
                .Select(claim => claim.Type)
                .OrderBy(type => type)]);
        Assert.Empty(ticket.Properties.GetTokens());
        var expectedPropertyKeys = new[]
        {
            ".expires",
            ".issued",
            ".monkado.currentSessionId",
            ".monkado.expectedMemberId",
            ".monkado.flowBinding",
            ".monkado.flowId",
            ".monkado.rememberMe",
            ".monkado.returnPath",
            ".redirect",
            ".refresh"
        };
        Assert.Equal(
            expectedPropertyKeys
                .OrderBy(key => key),
            ticket.Properties.Items.Keys
                .OrderBy(key => key));
        Assert.DoesNotContain(
            ticket.Properties.Items.Values,
            value => value is not null &&
                (value.Contains(
                        "valid-code",
                        StringComparison.Ordinal) ||
                    value.Contains(
                        "google-access-token-not-persisted",
                        StringComparison.Ordinal)));
        Assert.Equal(
            $"{GoogleAuthenticationConstants.CompletionPath}?flow={flowBinding}",
            ticket.Properties.RedirectUri);
        Assert.Equal(
            GoogleAuthenticationConstants.TransientLifetime,
            ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc);
        Assert.Equal(
            HttpStatusCode.Redirect,
            completion.StatusCode);
        Assert.Equal(
            "https://app.example.test/my-lists",
            completion.Headers.Location?.OriginalString);
        Assert.Equal(
            "no-store",
            completion.Headers.CacheControl?.ToString());
        var refreshCookie = Assert.Single(
            completion.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=functional-google-refresh",
                StringComparison.Ordinal));
        Assert.Contains(
            "httponly",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "secure",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "samesite=strict",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "expires=",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            completion.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=;",
                StringComparison.Ordinal));
        var context = Assert.IsType<GoogleAuthenticationContext>(
            factory.GoogleSessionService.LastCompletionContext);
        Assert.Equal(
            7,
            context.FlowId.Version);
        Assert.Equal(
            expectedMemberId,
            context.ExpectedMemberId);
        Assert.Equal(
            currentSessionId,
            context.CurrentSessionId);
        Assert.True(context.IsPersistent);
        Assert.Equal(
            "/my-lists",
            context.ReturnPath);
        Assert.Equal(
            "functional-google-subject",
            context.Identity.Subject);
        Assert.Equal(
            1,
            factory.Backchannel.TokenRequestCount);
        var tokenRequest = QueryHelpers.ParseQuery(string.Concat(
            "?",
            factory.Backchannel.LastTokenRequestBody));
        Assert.Equal(
            "valid-code",
            Assert.Single(tokenRequest["code"]));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(tokenRequest["code_verifier"])));
        Assert.True(factory.Backchannel.WasPkceValidated);
        Assert.Equal(
            1,
            factory.GoogleSessionService.ResolveCallCount);

        using var replayClient = factory.CreateGoogleClient(handleCookies: false);
        using var replayRequest = new HttpRequestMessage(
            HttpMethod.Get,
            callback.Headers.Location);
        replayRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            externalCookie);
        using var replay = await replayClient.SendAsync(
            replayRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            replay.Headers.Location?.OriginalString);
        Assert.DoesNotContain(
            replay.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=",
                StringComparison.Ordinal));
        Assert.Contains(
            replay.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=;",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteAsync_WhenConcurrentCallbackOverwritesCookie_RejectsMismatchedFlowAndPreservesCurrentFlow()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var firstCallback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var firstFlowBinding = GetFlowBinding(firstCallback.Headers.Location);
        var secondProtocol = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = secondProtocol.nonce;
        using var secondCallback = await PostCallbackAsync(
            client,
            secondProtocol.state,
            TestContext.Current.CancellationToken);
        var secondFlowBinding = GetFlowBinding(secondCallback.Headers.Location);

        // Act
        using var mismatchedCompletion = await client.GetAsync(
            firstCallback.Headers.Location,
            TestContext.Current.CancellationToken);
        using var currentCompletion = await client.GetAsync(
            secondCallback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(
            firstFlowBinding,
            secondFlowBinding);
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            mismatchedCompletion.Headers.Location?.OriginalString);
        Assert.False(mismatchedCompletion.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            "https://app.example.test/my-lists",
            currentCompletion.Headers.Location?.OriginalString);
        Assert.Contains(
            currentCompletion.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=functional-google-refresh",
                StringComparison.Ordinal));
        Assert.Equal(
            1,
            factory.GoogleSessionService.CompletionCallCount);
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                    firstFlowBinding,
                    StringComparison.Ordinal) ||
                message.Contains(
                    secondFlowBinding,
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteAsync_WhenSameRemoteStateProducesTwoTickets_BindsEachCallbackIndependently()
    {
        // Arrange
        using var identityModelEvents = new CapturingIdentityModelEventListener();
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient(handleCookies: false);
        var (state, nonce, cookieHeader) = await StartManualFlowAsync(
            client,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        factory.Backchannel.Subject = "first-google-subject";
        using var firstCallbackRequest = CreateCallbackRequest(
            cookieHeader,
            state,
            "first-valid-code");
        using var firstCallback = await client.SendAsync(
            firstCallbackRequest,
            TestContext.Current.CancellationToken);
        var firstExternalCookie = ExtractResponseCookie(
            firstCallback,
            ExternalCookieName);
        var firstFlowBinding = GetFlowBinding(firstCallback.Headers.Location);
        factory.Backchannel.Subject = "second-google-subject";
        using var secondCallbackRequest = CreateCallbackRequest(
            cookieHeader,
            state,
            "second-valid-code");
        using var secondCallback = await client.SendAsync(
            secondCallbackRequest,
            TestContext.Current.CancellationToken);
        var secondExternalCookie = ExtractResponseCookie(
            secondCallback,
            ExternalCookieName);
        var secondFlowBinding = GetFlowBinding(secondCallback.Headers.Location);
        using var mismatchedRequest = CreateCompletionRequest(
            firstCallback.Headers.Location,
            secondExternalCookie);
        using var currentRequest = CreateCompletionRequest(
            secondCallback.Headers.Location,
            secondExternalCookie);

        // Act
        using var mismatchedCompletion = await client.SendAsync(
            mismatchedRequest,
            TestContext.Current.CancellationToken);
        using var currentCompletion = await client.SendAsync(
            currentRequest,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(
            firstExternalCookie,
            secondExternalCookie);
        Assert.NotEqual(
            firstFlowBinding,
            secondFlowBinding);
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            mismatchedCompletion.Headers.Location?.OriginalString);
        Assert.False(mismatchedCompletion.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            "https://app.example.test/my-lists",
            currentCompletion.Headers.Location?.OriginalString);
        Assert.Equal(
            "second-google-subject",
            factory.GoogleSessionService.LastCompletionContext?.Identity.Subject);
        Assert.Equal(
            1,
            factory.GoogleSessionService.CompletionCallCount);
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                    firstFlowBinding,
                    StringComparison.Ordinal) ||
                message.Contains(
                    secondFlowBinding,
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            identityModelEvents.Messages,
            message => message.Contains(
                    firstFlowBinding,
                    StringComparison.Ordinal) ||
                message.Contains(
                    secondFlowBinding,
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callback_WhenAnotherFlowFailsBeforeTicketIssuance_PreservesCurrentExternalCookie()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        var currentProtocol = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = currentProtocol.nonce;
        using var currentCallback = await PostCallbackAsync(
            client,
            currentProtocol.state,
            TestContext.Current.CancellationToken);
        using var failingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            string.Concat(
                GoogleAuthenticationConstants.CallbackPath,
                "?code=invalid-transport&state=",
                Uri.EscapeDataString(state)));

        // Act
        using var failingCallback = await client.SendAsync(
            failingRequest,
            TestContext.Current.CancellationToken);
        using var currentCompletion = await client.GetAsync(
            currentCallback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            failingCallback.Headers.Location?.OriginalString);
        Assert.DoesNotContain(
            failingCallback.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookies)
                ? setCookies
                : [],
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=",
                StringComparison.Ordinal));
        Assert.Equal(
            "https://app.example.test/my-lists",
            currentCompletion.Headers.Location?.OriginalString);
        Assert.Equal(
            1,
            factory.GoogleSessionService.CompletionCallCount);
    }

    [Fact]
    public async Task Callback_WhenExpectedMemberPresenceDiffers_UsesLengthIndistinguishableProtectedState()
    {
        // Arrange
        using var absentFactory = new GoogleAuthenticationApiFactory();
        using var absentClient = absentFactory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            absentClient,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        absentFactory.Backchannel.Nonce = nonce;
        using var absentCallback = await PostCallbackAsync(
            absentClient,
            state,
            TestContext.Current.CancellationToken);
        var absentCookie = GetCookiePair(
            absentCallback,
            ExternalCookieName);

        var expectedMemberId = Guid.CreateVersion7();
        using var presentFactory = new GoogleAuthenticationApiFactory();
        presentFactory.GoogleSessionService.ExpectedMemberId = expectedMemberId;
        using var presentClient = presentFactory.CreateGoogleClient();
        var presentProtocol = await StartFlowAsync(
            presentClient,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        presentFactory.Backchannel.Nonce = presentProtocol.nonce;
        using var presentCallback = await PostCallbackAsync(
            presentClient,
            presentProtocol.state,
            TestContext.Current.CancellationToken);
        var presentCookie = GetCookiePair(
            presentCallback,
            ExternalCookieName);

        // Act
        using var absentCompletion = await absentClient.GetAsync(
            absentCallback.Headers.Location,
            TestContext.Current.CancellationToken);
        using var presentCompletion = await presentClient.GetAsync(
            presentCallback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            absentCookie.Length,
            presentCookie.Length);
        Assert.Null(absentFactory.GoogleSessionService.LastCompletionContext?.ExpectedMemberId);
        Assert.Equal(
            expectedMemberId,
            presentFactory.GoogleSessionService.LastCompletionContext?.ExpectedMemberId);
        Assert.Equal(
            HttpStatusCode.Redirect,
            absentCompletion.StatusCode);
        Assert.Equal(
            HttpStatusCode.Redirect,
            presentCompletion.StatusCode);
    }

    [Fact]
    public async Task CompleteAsync_WhenFlowCrossesInstances_UsesSharedDataProtectionKeyRing()
    {
        // Arrange
        var keysPath = Path.Combine(
            Path.GetTempPath(),
            $"mon-kado-google-keys-{Guid.CreateVersion7():D}");
        Directory.CreateDirectory(keysPath);

        try
        {
            using var firstFactory = new GoogleAuthenticationApiFactory(
                dataProtectionKeysPath: keysPath);
            using var firstClient = firstFactory.CreateGoogleClient(handleCookies: false);
            using var challenge = await firstClient.GetAsync(
                "/api/v1/auth/google?returnPath=%2Fmy-lists&rememberMe=false",
                TestContext.Current.CancellationToken);
            var challengeLocation = Assert.IsType<Uri>(challenge.Headers.Location);
            var query = QueryHelpers.ParseQuery(challengeLocation.Query);
            var state = Assert.IsType<string>(Assert.Single(query["state"]));
            var nonce = Assert.IsType<string>(Assert.Single(query["nonce"]));
            var protocolCookies = string.Join(
                "; ",
                challenge.Headers
                    .GetValues("Set-Cookie")
                    .Where(cookie => cookie.StartsWith(
                            ".AspNetCore.Correlation.",
                            StringComparison.Ordinal) ||
                        cookie.StartsWith(
                            ".AspNetCore.OpenIdConnect.Nonce.",
                            StringComparison.Ordinal))
                    .Select(cookie => cookie.Split(
                        ';',
                        2)[0]));
            using var secondFactory = new GoogleAuthenticationApiFactory(
                dataProtectionKeysPath: keysPath);
            using var secondClient = secondFactory.CreateGoogleClient(handleCookies: false);
            secondFactory.Backchannel.Nonce = nonce;
            using var callbackRequest = new HttpRequestMessage(
                HttpMethod.Post,
                GoogleAuthenticationConstants.CallbackPath)
            {
                Content = new FormUrlEncodedContent(
                [
                    new("code", "cross-instance-code"),
                    new("state", state)
                ])
            };
            callbackRequest.Headers.TryAddWithoutValidation(
                "Cookie",
                protocolCookies);
            using var callback = await secondClient.SendAsync(
                callbackRequest,
                TestContext.Current.CancellationToken);
            var externalCookie = GetCookiePair(
                callback,
                ExternalCookieName);
            using var completionRequest = new HttpRequestMessage(
                HttpMethod.Get,
                callback.Headers.Location);
            completionRequest.Headers.TryAddWithoutValidation(
                "Cookie",
                externalCookie);

            // Act
            using var completion = await firstClient.SendAsync(
                completionRequest,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.StartsWith(
                $"{GoogleAuthenticationConstants.CompletionPath}?flow=",
                callback.Headers.Location?.OriginalString,
                StringComparison.Ordinal);
            Assert.Equal(
                1,
                secondFactory.Backchannel.TokenRequestCount);
            Assert.Equal(
                1,
                secondFactory.GoogleSessionService.ResolveCallCount);
            Assert.Equal(
                HttpStatusCode.Redirect,
                completion.StatusCode);
            Assert.Equal(
                "https://app.example.test/my-lists",
                completion.Headers.Location?.OriginalString);
            Assert.NotNull(firstFactory.GoogleSessionService.LastCompletionContext);
            Assert.Contains(
                completion.Headers.GetValues("Set-Cookie"),
                cookie => cookie.StartsWith(
                    $"{RefreshCookieName}=functional-google-refresh",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(
                keysPath,
                recursive: true);
        }
    }

    [Fact]
    public async Task Callback_WhenInstancesUseDifferentDataProtectionKeyRings_RejectsStateBeforeProviderOrPersistence()
    {
        // Arrange
        var firstKeysPath = Path.Combine(
            Path.GetTempPath(),
            $"mon-kado-google-keys-a-{Guid.CreateVersion7():D}");
        var secondKeysPath = Path.Combine(
            Path.GetTempPath(),
            $"mon-kado-google-keys-b-{Guid.CreateVersion7():D}");
        Directory.CreateDirectory(firstKeysPath);
        Directory.CreateDirectory(secondKeysPath);

        try
        {
            using var firstFactory = new GoogleAuthenticationApiFactory(
                dataProtectionKeysPath: firstKeysPath);
            using var firstClient = firstFactory.CreateGoogleClient(handleCookies: false);
            using var challenge = await firstClient.GetAsync(
                "/api/v1/auth/google?returnPath=%2Fmy-lists&rememberMe=false",
                TestContext.Current.CancellationToken);
            var challengeLocation = Assert.IsType<Uri>(challenge.Headers.Location);
            var query = QueryHelpers.ParseQuery(challengeLocation.Query);
            var state = Assert.IsType<string>(Assert.Single(query["state"]));
            var protocolCookies = string.Join(
                "; ",
                challenge.Headers
                    .GetValues("Set-Cookie")
                    .Where(cookie => cookie.StartsWith(
                            ".AspNetCore.Correlation.",
                            StringComparison.Ordinal) ||
                        cookie.StartsWith(
                            ".AspNetCore.OpenIdConnect.Nonce.",
                            StringComparison.Ordinal))
                    .Select(cookie => cookie.Split(
                        ';',
                        2)[0]));
            using var secondFactory = new GoogleAuthenticationApiFactory(
                dataProtectionKeysPath: secondKeysPath);
            using var secondClient = secondFactory.CreateGoogleClient(handleCookies: false);
            using var callbackRequest = new HttpRequestMessage(
                HttpMethod.Post,
                GoogleAuthenticationConstants.CallbackPath)
            {
                Content = new FormUrlEncodedContent(
                [
                    new("code", "cross-instance-code"),
                    new("state", state)
                ])
            };
            callbackRequest.Headers.TryAddWithoutValidation(
                "Cookie",
                protocolCookies);

            // Act
            using var callback = await secondClient.SendAsync(
                callbackRequest,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(
                HttpStatusCode.Redirect,
                callback.StatusCode);
            Assert.Equal(
                "https://app.example.test/#/login?error=google_auth_failed",
                callback.Headers.Location?.OriginalString);
            Assert.Equal(
                0,
                secondFactory.Backchannel.TokenRequestCount);
            Assert.Equal(
                0,
                secondFactory.GoogleSessionService.ResolveCallCount);
            Assert.Null(secondFactory.GoogleSessionService.LastCompletionContext);
            Assert.DoesNotContain(
                state,
                string.Join(
                    Environment.NewLine,
                    secondFactory.LogMessages),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(
                firstKeysPath,
                recursive: true);
            Directory.Delete(
                secondKeysPath,
                recursive: true);
        }
    }

    [Fact]
    public async Task CompleteAsync_WhenSessionCreationIsUnavailable_PreservesBothCookiesForRetry()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.GoogleSessionService.IsCompletionUnavailable = true;
        using var client = factory.CreateGoogleClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            $"{RefreshCookieName}=existing-refresh-canary");
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var flowBinding = GetFlowBinding(callback.Headers.Location);

        // Act
        using var unavailable = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);
        var error = await unavailable.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            unavailable.StatusCode);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error?.ErrorCode);
        Assert.Contains(
            "no-store",
            unavailable.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        Assert.False(unavailable.Headers.Contains("Set-Cookie"));
        factory.GoogleSessionService.IsCompletionUnavailable = false;
        using var retry = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "https://app.example.test/my-lists",
            retry.Headers.Location?.OriginalString);
        var retryRefreshCookie = Assert.Single(
            retry.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=functional-google-refresh",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            "expires=",
            retryRefreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "max-age=",
            retryRefreshCookie,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_WhenApplicationRejectsFlow_DeletesExternalCookieAndRedirectsSafely()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.GoogleSessionService.IsCompletionRejected = true;
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var flowBinding = GetFlowBinding(callback.Headers.Location);

        // Act
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            completion.Headers.Location?.OriginalString);
        Assert.Contains(
            completion.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=;",
                StringComparison.Ordinal));
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                    "[Error]",
                    StringComparison.Ordinal) &&
                message.Contains(
                    "ApplicationRejected",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData((int)GoogleAuthenticationOutcome.SessionCreated, true)]
    public async Task CompleteAsync_WhenCompletionResultIsInconsistent_DeletesExternalCookieAndRedirectsSafely(
        int outcome,
        bool returnNullSession)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.GoogleSessionService.CompletionOutcome = (GoogleAuthenticationOutcome)outcome;
        factory.GoogleSessionService.ReturnNullCompletionSession = returnNullSession;
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);

        // Act
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            completion.Headers.Location?.OriginalString);
        Assert.Contains(
            completion.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=;",
                StringComparison.Ordinal));
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                    "[Error]",
                    StringComparison.Ordinal) &&
                message.Contains(
                    "InvalidCompletionOutcome",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteAsync_WhenAdditionalVerificationIsRequired_PreservesExternalCookieForGenericLinkAttempt()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.GoogleSessionService.CompletionOutcome =
            GoogleAuthenticationOutcome.AdditionalVerificationRequired;
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var flowBinding = GetFlowBinding(callback.Headers.Location);

        // Act
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            completion.StatusCode);
        Assert.Equal(
            $"https://app.example.test/#/login?error=google_additional_verification_required&flow={flowBinding}",
            completion.Headers.Location?.OriginalString);
        Assert.False(completion.Headers.Contains("Set-Cookie"));
        var csrfToken = await GetAntiforgeryTokenAsync(
            client,
            TestContext.Current.CancellationToken);
        using var linked = await PostLinkAsync(
            client,
            csrfToken,
            "valid-current-password",
            flowBinding,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            linked.StatusCode);
    }

    [Fact]
    public async Task CompleteAsync_WhenExternalCookieHasExpired_RejectsFlowAndDeletesCookie()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        factory.TimeProvider.Advance(
            GoogleAuthenticationConstants.TransientLifetime.Add(TimeSpan.FromSeconds(1)));

        // Act
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            completion.StatusCode);
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            completion.Headers.Location?.OriginalString);
        Assert.Contains(
            completion.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=;",
                StringComparison.Ordinal));
        Assert.Null(factory.GoogleSessionService.LastCompletionContext);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                    "[Error]",
                    StringComparison.Ordinal) &&
                message.Contains(
                    "Classification: InvalidExternalTicket",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteAsync_WhenCallbackOccursNearStateExpiry_GetsFreshExternalCookieLifetime()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.TimeProvider.Advance(
            GoogleAuthenticationConstants.TransientLifetime.Subtract(TimeSpan.FromSeconds(10)));
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        factory.TimeProvider.Advance(
            GoogleAuthenticationConstants.TransientLifetime.Subtract(TimeSpan.FromSeconds(1)));

        // Act
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            completion.StatusCode);
        Assert.Equal(
            "https://app.example.test/my-lists",
            completion.Headers.Location?.OriginalString);
        Assert.NotNull(factory.GoogleSessionService.LastCompletionContext);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Callback_WhenTransportIsNotFormUrlEncodedPost_RejectsBeforeProviderOrPersistence(
        bool usesGet)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        using var request = CreateInvalidCallbackTransportRequest(
            usesGet,
            state);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            response.Headers.Location?.OriginalString);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
        var sensitiveMessages = factory.LogMessages
            .Where(message => message.Contains(
                state,
                StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(sensitiveMessages);
    }

    [Fact]
    public async Task Callback_WhenRequestUsesHttp_RejectsBeforeProviderOrPersistence()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var httpsClient = factory.CreateGoogleClient(handleCookies: false);
        using var challenge = await httpsClient.GetAsync(
            "/api/v1/auth/google?returnPath=%2Fmy-lists&rememberMe=false",
            TestContext.Current.CancellationToken);
        var challengeLocation = Assert.IsType<Uri>(challenge.Headers.Location);
        var query = QueryHelpers.ParseQuery(challengeLocation.Query);
        var state = Assert.IsType<string>(Assert.Single(query["state"]));
        var protocolCookies = string.Join(
            "; ",
            challenge.Headers
                .GetValues("Set-Cookie")
                .Where(cookie => cookie.StartsWith(
                        ".AspNetCore.Correlation.",
                        StringComparison.Ordinal) ||
                    cookie.StartsWith(
                        ".AspNetCore.OpenIdConnect.Nonce.",
                        StringComparison.Ordinal))
                .Select(cookie => cookie.Split(
                    ';',
                    2)[0]));
        using var httpClient = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("http://localhost"),
                HandleCookies = false
            });
        using var callbackRequest = new HttpRequestMessage(
            HttpMethod.Post,
            GoogleAuthenticationConstants.CallbackPath)
        {
            Content = new FormUrlEncodedContent(
            [
                new("code", "valid-code"),
                new("state", state)
            ])
        };
        callbackRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            protocolCookies);

        // Act
        using var callback = await httpClient.SendAsync(
            callbackRequest,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            callback.StatusCode);
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            callback.Headers.Location?.OriginalString);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
    }

    [Fact]
    public async Task Callback_WhenNonceIsInvalid_RejectsBeforeExpectedMemberResolution()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = string.Concat(
            nonce,
            "-tampered");

        // Act
        using var response = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            response.Headers.Location?.OriginalString);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
        Assert.DoesNotContain(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                    $"{ExternalCookieName}=",
                    StringComparison.Ordinal) &&
                !cookie.StartsWith(
                    $"{ExternalCookieName}=;",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unsigned")]
    [InlineData("wrong-rsa-key")]
    [InlineData("hs256")]
    public async Task Callback_WhenIdentityTokenSignatureIsNotValidatedRs256_RejectsBeforePersistence(
        string tokenMode)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.Backchannel.UseUnsignedIdentityToken = tokenMode == "unsigned";
        factory.Backchannel.UseInvalidSigningKey = tokenMode == "wrong-rsa-key";
        factory.Backchannel.UseHmacSigningKey = tokenMode == "hs256";
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;

        // Act
        using var response = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            response.Headers.Location?.OriginalString);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
        Assert.DoesNotContain(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                    $"{ExternalCookieName}=",
                    StringComparison.Ordinal) &&
                !cookie.StartsWith(
                    $"{ExternalCookieName}=;",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callback_WhenPkceVerifierDoesNotMatchChallenge_RejectsBeforeIdentityValidation()
    {
        // Arrange
        const string codeCanary = "pkce-code-canary";
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        factory.Backchannel.ExpectedCodeChallenge = string.Concat(
            codeChallenge,
            "altered");
        using var content = new FormUrlEncodedContent(
        [
            new("code", codeCanary),
            new("state", state)
        ]);

        // Act
        using var callback = await client.PostAsync(
            GoogleAuthenticationConstants.CallbackPath,
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            callback.Headers.Location?.OriginalString);
        Assert.Equal(
            1,
            factory.Backchannel.TokenRequestCount);
        Assert.False(factory.Backchannel.WasPkceValidated);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
        Assert.DoesNotContain(
            codeCanary,
            string.Join(
                Environment.NewLine,
                factory.LogMessages),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-state")]
    [InlineData("altered-state")]
    [InlineData("missing-correlation-cookie")]
    public async Task Callback_WhenStateOrCorrelationIsInvalid_RejectsBeforeBackchannel(
        string scenario)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        var handleCookies = scenario != "missing-correlation-cookie";
        using var client = factory.CreateGoogleClient(handleCookies);
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        var form = new Dictionary<string, string>
        {
            ["code"] = "state-validation-code"
        };

        if (scenario != "missing-state")
            form["state"] = scenario == "altered-state"
                ? string.Concat(
                    state,
                    "tampered")
                : state;

        using var content = new FormUrlEncodedContent(form);

        // Act
        using var response = await client.PostAsync(
            GoogleAuthenticationConstants.CallbackPath,
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            response.Headers.Location?.OriginalString);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
    }

    [Fact]
    public async Task Callback_WhenRemoteFlowHasExpired_RejectsBeforeBackchannelAndClearsCorrelationCookie()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.TimeProvider.Advance(
            GoogleAuthenticationConstants.TransientLifetime.Add(TimeSpan.FromSeconds(1)));

        // Act
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            callback.Headers.Location?.OriginalString);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
        Assert.Contains(
            callback.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                    ".AspNetCore.Correlation.",
                    StringComparison.Ordinal) &&
                cookie.Contains(
                    "=;",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("missing-iat")]
    [InlineData("future-iat")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("azp")]
    [InlineData("email-unverified")]
    public async Task Callback_WhenIdentityTokenContractIsInvalid_RejectsBeforePersistence(
        string scenario)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.Backchannel.IdentityTokenAge = scenario switch
        {
            "expired" => TimeSpan.FromMinutes(10),
            "future-iat" => TimeSpan.FromMinutes(-1),
            _ => TimeSpan.Zero
        };
        factory.Backchannel.IncludeIssuedAt = scenario != "missing-iat";
        factory.Backchannel.IdentityTokenIssuer = scenario == "issuer"
            ? "https://issuer.example.test"
            : GoogleAuthenticationApiFactory.Issuer;
        factory.Backchannel.IdentityTokenAudience = scenario == "audience"
            ? "another-client"
            : GoogleAuthenticationApiFactory.ClientId;
        factory.Backchannel.AuthorizedParty = scenario == "azp"
            ? "another-client"
            : GoogleAuthenticationApiFactory.ClientId;
        factory.Backchannel.EmailVerified = scenario != "email-unverified";
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;

        // Act
        using var response = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            response.Headers.Location?.OriginalString);
        Assert.Equal(
            1,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
    }

    [Fact]
    public async Task Callback_WhenAlternateGoogleIssuerIsValid_AcceptsDocumentedIssuerVariant()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.Backchannel.IdentityTokenIssuer = GoogleAuthenticationConstants.AlternateIssuer;
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;

        // Act
        using var response = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);
        Assert.StartsWith(
            $"{GoogleAuthenticationConstants.CompletionPath}?flow=",
            response.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=",
                StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(InvalidProviderDisplayNames))]
    public async Task CompleteAsync_WhenProviderDisplayNameIsInvalid_OmitsOptionalClaimAndCreatesSession(
        string displayName)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.Backchannel.DisplayName = displayName;
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var externalCookie = GetCookiePair(
            callback,
            ExternalCookieName);
        var cookieValue = Uri.UnescapeDataString(externalCookie.Split(
            '=',
            2)[1]);
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(GoogleAuthenticationSchemes.ExternalCookie);
        var ticket = Assert.IsType<AuthenticationTicket>(
            cookieOptions.TicketDataFormat.Unprotect(cookieValue));

        // Act
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(
            ticket.Principal.Claims,
            claim => claim.Type == "name");
        Assert.Equal(
            HttpStatusCode.Redirect,
            completion.StatusCode);
        Assert.Equal(
            "https://app.example.test/my-lists",
            completion.Headers.Location?.OriginalString);
        Assert.Null(factory.GoogleSessionService.LastCompletionContext?.Identity.DisplayName);
    }

    [Fact]
    public async Task Callback_WhenExpectedMemberResolutionIsUnavailable_RedirectsRetryablyWithoutSensitiveData()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.GoogleSessionService.IsResolutionUnavailable = true;
        using var client = factory.CreateGoogleClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            $"{RefreshCookieName}=existing-refresh-canary");
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;

        // Act
        using var response = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_authentication_unavailable",
            response.Headers.Location?.OriginalString);
        Assert.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                    $"{ExternalCookieName}=",
                    StringComparison.Ordinal) &&
                !cookie.StartsWith(
                    $"{ExternalCookieName}=;",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=",
                StringComparison.Ordinal));
        Assert.Equal(
            "existing-refresh-canary",
            factory.RefreshSessionService.LastRefreshToken);
        var logs = string.Join(
            Environment.NewLine,
            factory.LogMessages);
        Assert.DoesNotContain(
            "member@gmail.com",
            logs,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "functional-google-subject",
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "google-access-token-not-persisted",
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "functional-client-secret",
            logs,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Callback_WhenProviderResponseContainsSensitiveFailure_DoesNotLogProtocolValues(
        bool useInvalidIdentityToken)
    {
        // Arrange
        const string failureCanary = "provider-sensitive-canary";
        using var identityModelEvents = new CapturingIdentityModelEventListener();
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;

        if (useInvalidIdentityToken)
            factory.Backchannel.IdentityTokenOverride = failureCanary;

        using var content = CreateProviderFailureContent(
            useInvalidIdentityToken,
            failureCanary,
            state);

        // Act
        using var response = await client.PostAsync(
            GoogleAuthenticationConstants.CallbackPath,
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            response.Headers.Location?.OriginalString);

        var logs = string.Join(
            Environment.NewLine,
            factory.LogMessages);
        Assert.DoesNotContain(
            failureCanary,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            state,
            logs,
            StringComparison.Ordinal);
        var identityModelLogs = string.Join(
            Environment.NewLine,
            identityModelEvents.Messages);
        Assert.DoesNotContain(
            failureCanary,
            identityModelLogs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            state,
            identityModelLogs,
            StringComparison.Ordinal);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
    }

    [Fact]
    public async Task Callback_WhenProviderConfigurationBecomesUnavailable_UsesSafeTransientFailure()
    {
        // Arrange
        const string codeCanary = "provider-configuration-code-canary";
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        var openIdConnectOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(GoogleAuthenticationSchemes.OpenIdConnect);
        openIdConnectOptions.Configuration = null;
        openIdConnectOptions.ConfigurationManager = new GoogleOpenIdConnectConfigurationManager(
            new UnavailableOpenIdConnectConfigurationManager());
        using var content = new FormUrlEncodedContent(
        [
            new("code", codeCanary),
            new("state", state)
        ]);

        // Act
        using var callback = await client.PostAsync(
            GoogleAuthenticationConstants.CallbackPath,
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_authentication_unavailable",
            callback.Headers.Location?.OriginalString);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                    "[Error]",
                    StringComparison.Ordinal) &&
                message.Contains(
                    "Google authentication provider is temporarily unavailable. " +
                    "Failure type: ProviderConfiguration.",
                    StringComparison.Ordinal));
        var logs = string.Join(
            Environment.NewLine,
            factory.LogMessages);
        Assert.DoesNotContain(
            codeCanary,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            state,
            logs,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_WhenTokenEndpointIsUnavailable_UsesSafeTransientFailureWithoutRetry()
    {
        // Arrange
        const string codeCanary = "provider-token-code-canary";
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            $"{RefreshCookieName}=current-refresh-canary");
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.IsTokenEndpointUnavailable = true;
        using var content = new FormUrlEncodedContent(
        [
            new("code", codeCanary),
            new("state", state)
        ]);

        // Act
        using var callback = await client.PostAsync(
            GoogleAuthenticationConstants.CallbackPath,
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_authentication_unavailable",
            callback.Headers.Location?.OriginalString);
        Assert.Equal(
            1,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
        Assert.DoesNotContain(
            callback.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookieValues)
                    ? setCookieValues
                    : [],
            value => value.StartsWith(
                RefreshCookieName,
                StringComparison.Ordinal));
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                    "[Error]",
                    StringComparison.Ordinal) &&
                message.Contains(
                    "Failure type: HttpRequestException.",
                    StringComparison.Ordinal));
        var logs = string.Join(
            Environment.NewLine,
            factory.LogMessages);
        Assert.DoesNotContain(
            codeCanary,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            state,
            logs,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Callback_WhenTokenEndpointReturnsTransientStatus_UsesSafeTransientFailureWithoutRetry(
        HttpStatusCode statusCode)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.TokenEndpointStatusCode = statusCode;

        // Act
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_authentication_unavailable",
            callback.Headers.Location?.OriginalString);
        Assert.Equal(
            1,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
        Assert.DoesNotContain(
            callback.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookieValues)
                ? setCookieValues
                : [],
            value => value.StartsWith(
                $"{ExternalCookieName}=",
                StringComparison.Ordinal));
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                    "[Error]",
                    StringComparison.Ordinal) &&
                message.Contains(
                    "Failure type: HttpRequestException.",
                    StringComparison.Ordinal));
        var logs = string.Join(
            Environment.NewLine,
            factory.LogMessages);
        Assert.DoesNotContain(
            "provider-response-canary",
            logs,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_WhenRateLimitIsExceeded_DoesNotReachProviderOrPersistenceAgain()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient(handleCookies: false);
        var (state, nonce, cookieHeader) = await StartManualFlowAsync(
            client,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;

        // Act
        for (var index = 0;
             index < AuthenticationRateLimitingExtensions.GoogleTransientFlowPermitLimit;
             index++)
        {
            using var acceptedRequest = CreateCallbackRequest(
                cookieHeader,
                state,
                string.Concat(
                    "rate-limit-code-",
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            using var acceptedResponse = await client.SendAsync(
                acceptedRequest,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.Redirect,
                acceptedResponse.StatusCode);
        }

        var tokenRequestCount = factory.Backchannel.TokenRequestCount;
        var resolveCallCount = factory.GoogleSessionService.ResolveCallCount;
        using var rejectedRequest = new HttpRequestMessage(
            HttpMethod.Post,
            GoogleAuthenticationConstants.CallbackPath)
        {
            Content = new UnknownLengthContent(new string(
                'x',
                5 * 1024))
        };
        rejectedRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/x-www-form-urlencoded");
        rejectedRequest.Headers.TransferEncodingChunked = true;
        rejectedRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            cookieHeader);
        using var rejectedResponse = await client.SendAsync(
            rejectedRequest,
            TestContext.Current.CancellationToken);
        var error = await rejectedResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejectedResponse.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestRateLimitExceeded,
            error?.ErrorCode);
        Assert.Equal(
            tokenRequestCount,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            resolveCallCount,
            factory.GoogleSessionService.ResolveCallCount);
        Assert.Equal(
            AuthenticationRateLimitingExtensions.GoogleTransientFlowPermitLimit,
            tokenRequestCount);
    }

    [Fact]
    public async Task CompleteAsync_WhenRateLimitIsExceeded_DoesNotReachPersistenceAgain()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient(handleCookies: false);
        var (state, nonce, cookieHeader) = await StartManualFlowAsync(
            client,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callbackRequest = CreateCallbackRequest(
            cookieHeader,
            state,
            "completion-rate-limit-code");
        using var callbackResponse = await client.SendAsync(
            callbackRequest,
            TestContext.Current.CancellationToken);
        var externalCookie = ExtractResponseCookie(
            callbackResponse,
            ExternalCookieName);

        // Act
        for (var index = 0;
             index < AuthenticationRateLimitingExtensions.GoogleTransientFlowPermitLimit;
             index++)
        {
            using var acceptedRequest = CreateCompletionRequest(
                callbackResponse.Headers.Location,
                externalCookie);
            using var acceptedResponse = await client.SendAsync(
                acceptedRequest,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.Redirect,
                acceptedResponse.StatusCode);
        }

        var completionCallCount = factory.GoogleSessionService.CompletionCallCount;
        using var rejectedRequest = CreateCompletionRequest(
            callbackResponse.Headers.Location,
            externalCookie);
        using var rejectedResponse = await client.SendAsync(
            rejectedRequest,
            TestContext.Current.CancellationToken);
        var error = await rejectedResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejectedResponse.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestRateLimitExceeded,
            error?.ErrorCode);
        Assert.Equal(
            completionCallCount,
            factory.GoogleSessionService.CompletionCallCount);
        Assert.Equal(
            AuthenticationRateLimitingExtensions.GoogleTransientFlowPermitLimit,
            completionCallCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Callback_WhenBodyExceedsLimit_ReturnsStructuredPayloadTooLargeBeforeBackchannel(
        bool hasKnownLength,
        bool hasTrailingSlash)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient(handleCookies: false);
        var body = string.Concat(
            "code=",
            new string(
                'x',
                5 * 1024));
        using var content = CreateBodyContent(
            hasKnownLength,
            body,
            "application/x-www-form-urlencoded");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            hasTrailingSlash
                ? $"{GoogleAuthenticationConstants.CallbackPath}/"
                : GoogleAuthenticationConstants.CallbackPath)
        {
            Content = content
        };

        if (!hasKnownLength)
            request.Headers.TransferEncodingChunked = true;

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            error?.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestPayloadTooLarge,
            error?.ErrorCode);
        Assert.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
    }

    [Fact]
    public async Task Callback_WhenChunkedBodyIsWithinLimit_RewindsBodyBeforeProtocolValidation()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        var body = string.Concat(
            "code=valid-code&state=",
            Uri.EscapeDataString(state));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            GoogleAuthenticationConstants.CallbackPath)
        {
            Content = new UnknownLengthContent(body)
        };
        request.Headers.TransferEncodingChunked = true;

        // Act
        using var callback = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith(
            $"{GoogleAuthenticationConstants.CompletionPath}?flow=",
            callback.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            factory.Backchannel.TokenRequestCount);
        Assert.Equal(
            1,
            factory.GoogleSessionService.ResolveCallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LinkAsync_WhenBodyExceedsLimit_ReturnsStructuredPayloadTooLargeBeforeAuthentication(
        bool hasKnownLength)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient(handleCookies: false);
        var body = string.Concat(
            "{\"currentPassword\":\"",
            new string(
                'x',
                5 * 1024),
            "\"}");
        using var content = CreateBodyContent(
            hasKnownLength,
            body,
            "application/json");
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/json");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/google/link")
        {
            Content = content
        };

        if (!hasKnownLength)
            request.Headers.TransferEncodingChunked = true;

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestPayloadTooLarge,
            error?.ErrorCode);
        Assert.Equal(
            0,
            factory.GoogleSessionService.ResolveCallCount);
        Assert.Equal(
            0,
            factory.Backchannel.TokenRequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_WhenExternalCookieIsMissingOrAltered_ReturnsUnauthorizedAndDeletesCookie(
        bool hasAlteredCookie)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient(handleCookies: false);
        var antiforgery = await GetAntiforgeryContextAsync(
            client,
            TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/google/link")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = "valid-current-password"
            })
        };
        request.Headers.TryAddWithoutValidation(
            WebSecurityOptions.AntiforgeryHeaderName,
            antiforgery.Token);
        var cookieHeader = hasAlteredCookie
            ? $"{antiforgery.Cookie}; {ExternalCookieName}=altered-cookie"
            : antiforgery.Cookie;
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            cookieHeader);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.GoogleAuthenticationFailed,
            error?.ErrorCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=;",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=",
                StringComparison.Ordinal));
        Assert.Equal(
            0,
            factory.GoogleSessionService.LinkCallCount);
    }

    [Fact]
    public async Task LinkAsync_WhenConcurrentCallbackOverwritesCookie_RejectsMismatchedFlowAndPreservesCurrentFlow()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var firstCallback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var firstFlowBinding = GetFlowBinding(firstCallback.Headers.Location);
        var secondProtocol = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = secondProtocol.nonce;
        using var secondCallback = await PostCallbackAsync(
            client,
            secondProtocol.state,
            TestContext.Current.CancellationToken);
        var secondFlowBinding = GetFlowBinding(secondCallback.Headers.Location);
        var antiforgeryToken = await GetAntiforgeryTokenAsync(
            client,
            TestContext.Current.CancellationToken);

        // Act
        using var mismatchedLink = await PostLinkAsync(
            client,
            antiforgeryToken,
            "valid-current-password",
            firstFlowBinding,
            TestContext.Current.CancellationToken);
        using var currentLink = await PostLinkAsync(
            client,
            antiforgeryToken,
            "valid-current-password",
            secondFlowBinding,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            mismatchedLink.StatusCode);
        var error = await mismatchedLink.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ErrorCodes.GoogleAccountLinkFailed,
            error?.ErrorCode);
        Assert.False(mismatchedLink.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            HttpStatusCode.OK,
            currentLink.StatusCode);
        Assert.Contains(
            currentLink.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=functional-linked-refresh",
                StringComparison.Ordinal));
        Assert.Equal(
            1,
            factory.GoogleSessionService.LinkCallCount);
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                    firstFlowBinding,
                    StringComparison.Ordinal) ||
                message.Contains(
                    secondFlowBinding,
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task LinkAsync_WhenCurrentPasswordIsMissing_ReturnsStructuredValidationErrorAndPreservesCookie()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (antiforgeryToken, flowBinding) = await PrepareExplicitLinkFlowAsync(
            factory,
            client,
            TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildLinkPath(flowBinding))
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.TryAddWithoutValidation(
            WebSecurityOptions.AntiforgeryHeaderName,
            antiforgeryToken);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error?.ErrorCode);
        Assert.Contains(
            error?.ValidationErrors ?? [],
            validation => validation.PropertyName == "currentPassword");
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            0,
            factory.GoogleSessionService.LinkCallCount);
    }

    [Fact]
    public async Task LinkAsync_WhenContentTypeIsNotJson_ReturnsUnsupportedMediaTypeAndPreservesCookie()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (antiforgeryToken, flowBinding) = await PrepareExplicitLinkFlowAsync(
            factory,
            client,
            TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildLinkPath(flowBinding))
        {
            Content = new StringContent(
                "valid-current-password",
                Encoding.UTF8,
                "text/plain")
        };
        request.Headers.TryAddWithoutValidation(
            WebSecurityOptions.AntiforgeryHeaderName,
            antiforgeryToken);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestUnsupportedMediaType,
            error?.ErrorCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            0,
            factory.GoogleSessionService.LinkCallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("altered-antiforgery-token")]
    public async Task LinkAsync_WhenAntiforgeryTokenIsMissingOrAltered_ReturnsBadRequestAndPreservesCookie(
        string? antiforgeryToken)
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        var (_, flowBinding) = await PrepareExplicitLinkFlowAsync(
            factory,
            client,
            TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildLinkPath(flowBinding))
        {
            Content = JsonContent.Create(new
            {
                currentPassword = "valid-current-password"
            })
        };

        if (antiforgeryToken is not null)
            request.Headers.TryAddWithoutValidation(
                WebSecurityOptions.AntiforgeryHeaderName,
                antiforgeryToken);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            0,
            factory.GoogleSessionService.LinkCallCount);
    }

    [Fact]
    public async Task LinkAsync_WhenRateLimitIsExceeded_ReturnsStructuredTooManyRequestsAndPreservesCookie()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.GoogleSessionService.LinkOutcome =
            GoogleAccountLinkOutcome.InvalidCredentials;
        using var client = factory.CreateGoogleClient();
        var (antiforgeryToken, flowBinding) = await PrepareExplicitLinkFlowAsync(
            factory,
            client,
            TestContext.Current.CancellationToken);

        for (var requestIndex = 0; requestIndex < 5; requestIndex++)
        {
            using var rejected = await PostLinkAsync(
                client,
                antiforgeryToken,
                "invalid-password",
                flowBinding,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                rejected.StatusCode);
        }

        // Act
        using var response = await PostLinkAsync(
            client,
            antiforgeryToken,
            "invalid-password",
            flowBinding,
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestRateLimitExceeded,
            error?.ErrorCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            5,
            factory.GoogleSessionService.LinkCallCount);
    }

    [Fact]
    public async Task LinkAsync_WhenPasswordIsInvalid_PreservesExternalCookieForSafeRetry()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.GoogleSessionService.CompletionOutcome =
            GoogleAuthenticationOutcome.ExplicitLinkRequired;
        factory.GoogleSessionService.LinkOutcome =
            GoogleAccountLinkOutcome.InvalidCredentials;
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: true,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var flowBinding = GetFlowBinding(callback.Headers.Location);
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);
        var csrfToken = await GetAntiforgeryTokenAsync(
            client,
            TestContext.Current.CancellationToken);

        // Act
        using var rejected = await PostLinkAsync(
            client,
            csrfToken,
            "invalid-password",
            flowBinding,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            rejected.StatusCode);
        var rejectedError = await rejected.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ErrorCodes.GoogleAccountLinkFailed,
            rejectedError?.ErrorCode);
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
        factory.GoogleSessionService.LinkOutcome = GoogleAccountLinkOutcome.Success;
        using var accepted = await PostLinkAsync(
            client,
            csrfToken,
            "valid-current-password",
            flowBinding,
            TestContext.Current.CancellationToken);
        var accessToken = await accepted.Content.ReadFromJsonAsync<AccessTokenResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            accepted.StatusCode);
        Assert.Equal(
            "functional-access-token",
            accessToken?.AccessToken);
        Assert.Equal(
            "Bearer",
            accessToken?.TokenType);
        Assert.Equal(
            900,
            accessToken?.ExpiresIn);
        Assert.Equal(
            "no-store",
            accepted.Headers.CacheControl?.ToString());
        Assert.Contains(
            accepted.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=functional-linked-refresh",
                StringComparison.Ordinal));
        Assert.Contains(
            accepted.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=;",
                StringComparison.Ordinal));
        var context = Assert.IsType<GoogleAuthenticationContext>(
            factory.GoogleSessionService.LastLinkContext);
        Assert.Equal(
            7,
            context.FlowId.Version);
        Assert.Equal(
            "valid-current-password",
            factory.GoogleSessionService.LastCurrentPassword);
    }

    [Fact]
    public async Task LinkAsync_WhenAccountStateConflicts_DeletesTerminalExternalCookie()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.GoogleSessionService.CompletionOutcome =
            GoogleAuthenticationOutcome.ExplicitLinkRequired;
        factory.GoogleSessionService.LinkOutcome = GoogleAccountLinkOutcome.Conflict;
        using var client = factory.CreateGoogleClient();
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var flowBinding = GetFlowBinding(callback.Headers.Location);
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);
        var csrfToken = await GetAntiforgeryTokenAsync(
            client,
            TestContext.Current.CancellationToken);

        // Act
        using var response = await PostLinkAsync(
            client,
            csrfToken,
            "valid-current-password",
            flowBinding,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ErrorCodes.GoogleAccountLinkConflict,
            error?.ErrorCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{ExternalCookieName}=;",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task LinkAsync_WhenPersistenceIsUnavailable_PreservesExternalCookieForRetry()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        factory.GoogleSessionService.CompletionOutcome =
            GoogleAuthenticationOutcome.ExplicitLinkRequired;
        factory.GoogleSessionService.IsLinkUnavailable = true;
        using var client = factory.CreateGoogleClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Cookie",
            $"{RefreshCookieName}=existing-refresh-canary");
        var (state, nonce, codeChallenge) = await StartFlowAsync(
            client,
            rememberMe: false,
            TestContext.Current.CancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            TestContext.Current.CancellationToken);
        var flowBinding = GetFlowBinding(callback.Headers.Location);
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            TestContext.Current.CancellationToken);
        var csrfToken = await GetAntiforgeryTokenAsync(
            client,
            TestContext.Current.CancellationToken);

        // Act
        using var unavailable = await PostLinkAsync(
            client,
            csrfToken,
            "valid-current-password",
            flowBinding,
            TestContext.Current.CancellationToken);
        var error = await unavailable.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            unavailable.StatusCode);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error?.ErrorCode);
        Assert.False(unavailable.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            "existing-refresh-canary",
            factory.RefreshSessionService.LastRefreshToken);
        factory.GoogleSessionService.IsLinkUnavailable = false;
        using var retry = await PostLinkAsync(
            client,
            csrfToken,
            "valid-current-password",
            flowBinding,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            retry.StatusCode);
        Assert.Contains(
            retry.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                $"{RefreshCookieName}=functional-linked-refresh",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenGoogleContractIsGenerated_DocumentsFormPostCookiesAndNoStore()
    {
        // Arrange
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response body is empty.");
        var paths = document.RootElement.GetProperty("paths");
        var challenge = paths
            .GetProperty("/api/v1/auth/google")
            .GetProperty("get");
        var callback = paths
            .GetProperty(GoogleAuthenticationConstants.CallbackPath)
            .GetProperty("post");
        var link = paths
            .GetProperty("/api/v1/auth/google/link")
            .GetProperty("post");

        // Assert
        Assert.False(paths.TryGetProperty(
            GoogleAuthenticationConstants.CompletionPath,
            out _));
        Assert.Equal(
            "Starts Google sign-in with Authorization Code, PKCE, state and nonce.",
            challenge.GetProperty("summary").GetString());
        var challengeParameters = challenge.GetProperty("parameters").EnumerateArray().ToArray();
        var rememberMe = Assert.Single(
            challengeParameters,
            parameter => parameter.GetProperty("name").GetString() == "rememberMe");
        Assert.False(
            rememberMe.TryGetProperty(
                "required",
                out var required) &&
            required.GetBoolean());
        Assert.False(rememberMe
            .GetProperty("schema")
            .GetProperty("default")
            .GetBoolean());
        Assert.True(challenge
            .GetProperty("responses")
            .GetProperty("302")
            .GetProperty("headers")
            .TryGetProperty(
                "Cache-Control",
                out _));
        Assert.True(challenge
            .GetProperty("responses")
            .GetProperty("302")
            .GetProperty("headers")
            .TryGetProperty(
                "Location",
                out _));
        Assert.True(callback
            .GetProperty("responses")
            .GetProperty("302")
            .GetProperty("headers")
            .TryGetProperty(
                "Location",
                out _));
        var callbackLocationDescription = callback
            .GetProperty("responses")
            .GetProperty("302")
            .GetProperty("headers")
            .GetProperty("Location")
            .GetProperty("description")
            .GetString();
        Assert.Contains(
            "opaque flow binding",
            callbackLocationDescription,
            StringComparison.Ordinal);
        var callbackSetCookie = callback
            .GetProperty("responses")
            .GetProperty("302")
            .GetProperty("headers")
            .GetProperty("Set-Cookie")
            .GetProperty("description")
            .GetString();
        Assert.Contains(
            "five-minute",
            callbackSetCookie,
            StringComparison.Ordinal);
        Assert.Contains(
            "HttpOnly",
            callbackSetCookie,
            StringComparison.Ordinal);
        Assert.Contains(
            "SameSite=Lax",
            callbackSetCookie,
            StringComparison.Ordinal);
        Assert.Contains(
            "no Google token",
            callbackSetCookie,
            StringComparison.Ordinal);
        Assert.True(callback
            .GetProperty("requestBody")
            .GetProperty("content")
            .TryGetProperty(
                "application/x-www-form-urlencoded",
                out _));
        Assert.True(callback
            .GetProperty("responses")
            .TryGetProperty(
                "413",
                out _));
        var linkParameters = link.GetProperty("parameters").EnumerateArray().ToArray();
        var externalCookie = Assert.Single(
            linkParameters,
            parameter => parameter.GetProperty("name").GetString() ==
                GoogleAuthenticationConstants.ProductionExternalCookieName);
        Assert.True(externalCookie.GetProperty("required").GetBoolean());
        var flow = Assert.Single(
            linkParameters,
            parameter => parameter.GetProperty("name").GetString() ==
                GoogleAuthenticationConstants.FlowBindingParameter);
        Assert.True(flow.GetProperty("required").GetBoolean());
        Assert.Contains(
            "not an access, refresh or Google token",
            flow.GetProperty("description").GetString(),
            StringComparison.Ordinal);
        Assert.Single(
            linkParameters,
            parameter => parameter.GetProperty("name").GetString() ==
                WebSecurityOptions.AntiforgeryHeaderName);
        var successHeaders = link
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("headers");
        Assert.True(successHeaders.TryGetProperty(
            "Cache-Control",
            out _));
        Assert.True(successHeaders.TryGetProperty(
            "Set-Cookie",
            out _));
    }

    private static bool HasHardenedCrossSiteAttributes(string cookie)
    {

        return cookie.Contains(
                "httponly",
                StringComparison.OrdinalIgnoreCase) &&
            cookie.Contains(
                "secure",
                StringComparison.OrdinalIgnoreCase) &&
            cookie.Contains(
                "samesite=none",
                StringComparison.OrdinalIgnoreCase) &&
            !cookie.Contains(
                "domain=",
                StringComparison.OrdinalIgnoreCase);
    }

    private static FormUrlEncodedContent CreateProviderFailureContent(
        bool usesInvalidIdentityToken,
        string failureCanary,
        string state)
    {

        if (usesInvalidIdentityToken)
            return new FormUrlEncodedContent(
            [
                new("code", failureCanary),
                new("state", state)
            ]);

        return new FormUrlEncodedContent(
        [
            new("error", "access_denied"),
            new("error_description", failureCanary),
            new("state", state)
        ]);
    }

    private static HttpRequestMessage CreateInvalidCallbackTransportRequest(
        bool usesGet,
        string state)
    {

        if (usesGet)
            return new HttpRequestMessage(
                HttpMethod.Get,
                string.Concat(
                    GoogleAuthenticationConstants.CallbackPath,
                    "?code=valid-code&state=",
                    Uri.EscapeDataString(state)));

        return new HttpRequestMessage(
            HttpMethod.Post,
            GoogleAuthenticationConstants.CallbackPath)
        {
            Content = JsonContent.Create(new
            {
                code = "valid-code",
                state
            })
        };
    }

    private static HttpContent CreateBodyContent(
        bool hasKnownLength,
        string body,
        string mediaType)
    {

        if (!hasKnownLength)
            return new UnknownLengthContent(body);

        return new StringContent(
            body,
            Encoding.UTF8,
            mediaType);
    }

    private static async Task<(string state, string nonce, string cookieHeader)> StartManualFlowAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/api/v1/auth/google?returnPath=%2Fmy-lists&rememberMe=false",
            cancellationToken);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var query = QueryHelpers.ParseQuery(location.Query);
        var cookieHeader = string.Join(
            "; ",
            response.Headers
                .GetValues("Set-Cookie")
                .Select(cookie => cookie.Split(';', 2)[0]));

        return (
            Assert.IsType<string>(Assert.Single(query["state"])),
            Assert.IsType<string>(Assert.Single(query["nonce"])),
            cookieHeader);
    }

    private static HttpRequestMessage CreateCallbackRequest(
        string cookieHeader,
        string state,
        string code)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            GoogleAuthenticationConstants.CallbackPath)
        {
            Content = new FormUrlEncodedContent(
            [
                new("code", code),
                new("state", state)
            ])
        };
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            cookieHeader);

        return request;
    }

    private static HttpRequestMessage CreateCompletionRequest(
        Uri? completionLocation,
        string externalCookie)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            completionLocation);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            externalCookie);

        return request;
    }

    private static string ExtractResponseCookie(
        HttpResponseMessage response,
        string cookieName)
    {
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                string.Concat(
                    cookieName,
                    "="),
                StringComparison.Ordinal));

        return cookie.Split(';', 2)[0];
    }

    private static async Task<(string state, string nonce, string codeChallenge)> StartFlowAsync(
        HttpClient client,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"/api/v1/auth/google?returnPath=%2Fmy-lists&rememberMe={rememberMe}",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var query = QueryHelpers.ParseQuery(location.Query);

        var state = Assert.IsType<string>(Assert.Single(query["state"]));
        var nonce = Assert.IsType<string>(Assert.Single(query["nonce"]));
        var codeChallenge = Assert.IsType<string>(Assert.Single(query["code_challenge"]));

        return (
            state,
            nonce,
            codeChallenge);
    }

    private static Task<HttpResponseMessage> PostCallbackAsync(
        HttpClient client,
        string state,
        CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(
        [
            new("code", "valid-code"),
            new("state", state)
        ]);

        return client.PostAsync(
            GoogleAuthenticationConstants.CallbackPath,
            content,
            cancellationToken);
    }

    private static async Task<(string antiforgeryToken, string flowBinding)> PrepareExplicitLinkFlowAsync(
        GoogleAuthenticationApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        factory.GoogleSessionService.CompletionOutcome =
            GoogleAuthenticationOutcome.ExplicitLinkRequired;

        var (state, nonce, _) = await StartFlowAsync(
            client,
            rememberMe: false,
            cancellationToken);
        factory.Backchannel.Nonce = nonce;
        using var callback = await PostCallbackAsync(
            client,
            state,
            cancellationToken);
        var flowBinding = GetFlowBinding(callback.Headers.Location);
        using var completion = await client.GetAsync(
            callback.Headers.Location,
            cancellationToken);
        Assert.Equal(
            $"https://app.example.test/#/login/link-google?flow={flowBinding}",
            completion.Headers.Location?.OriginalString);

        return (
            await GetAntiforgeryTokenAsync(
                client,
                cancellationToken),
            flowBinding);
    }

    private static async Task<(string Token, string Cookie)> GetAntiforgeryContextAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            cancellationToken);

        return (
            Assert.IsType<string>(payload?.Token),
            GetCookiePair(
                response,
                AntiforgeryCookieName));
    }

    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            cancellationToken);

        return Assert.IsType<string>(payload?.Token);
    }

    private static async Task<HttpResponseMessage> PostLinkAsync(
        HttpClient client,
        string antiforgeryToken,
        string currentPassword,
        string flowBinding,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildLinkPath(flowBinding))
        {
            Content = JsonContent.Create(new
            {
                currentPassword
            })
        };
        request.Headers.TryAddWithoutValidation(
            WebSecurityOptions.AntiforgeryHeaderName,
            antiforgeryToken);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static string BuildLinkPath(string flowBinding)
    {

        return QueryHelpers.AddQueryString(
            "/api/v1/auth/google/link",
            GoogleAuthenticationConstants.FlowBindingParameter,
            flowBinding);
    }

    private static string GetFlowBinding(Uri? completionLocation)
    {
        var location = Assert.IsType<Uri>(completionLocation);
        var absoluteLocation = location.IsAbsoluteUri
            ? location
            : new Uri(
                new Uri("https://localhost"),
                location);
        var query = QueryHelpers.ParseQuery(absoluteLocation.Query);
        Assert.True(
            query.TryGetValue(
                GoogleAuthenticationConstants.FlowBindingParameter,
                out var values),
            $"The completion location '{location.OriginalString}' does not contain a flow binding.");

        return Assert.IsType<string>(
            Assert.Single(values));
    }

    private static string GetCookiePair(
        HttpResponseMessage response,
        string cookieName)
    {
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                    $"{cookieName}=",
                    StringComparison.Ordinal) &&
                !value.StartsWith(
                    $"{cookieName}=;",
                    StringComparison.Ordinal));

        return cookie.Split(
            ';',
            2)[0];
    }
}
