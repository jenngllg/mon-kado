using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistParticipantTests
{
    private const string ShareSecret = "public-secret";

    [Fact]
    public async Task JoinAsync_WhenGuestIsNew_ReturnsCreatedParticipantAndPersistentCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory(
            remoteIpAddress: IPAddress.Parse("192.0.2.10"));
        using var client = factory.CreateClient();
        var shareLinkId = Guid.CreateVersion7();

        // Act
        using var response = await SendJoinAsync(
            client,
            shareLinkId,
            "  Guest Jenn  ",
            includeBody: true);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            $"http://localhost/api/v1/shared-wishlists/{shareLinkId}/participants/current",
            response.Headers.Location?.AbsoluteUri);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                $"{GuestSessionCookieService.LocalCookieName}=",
                StringComparison.Ordinal));
        Assert.Contains(
            "httponly",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "samesite=strict",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "path=/",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "expires=",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "secure",
            cookie,
            StringComparison.OrdinalIgnoreCase);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The participant response is empty.");
        Assert.Equal(
            [
                "id",
                "displayName"
            ],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            "Guest Jenn",
            document.RootElement.GetProperty("displayName").GetString());
        var join = Assert.Single(factory.WishlistParticipantService.Joins);
        Assert.Null(join.MemberId);
        Assert.Null(join.GuestToken);
        Assert.Equal(
            "  Guest Jenn  ",
            join.DisplayName);
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                factory.WishlistParticipantService.JoinResult.GuestToken ?? string.Empty,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task JoinAsync_WhenMemberIsAuthenticated_AcceptsAnEmptyBodyWithoutIssuingGuestCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        factory.WishlistParticipantService.JoinResult = new WishlistParticipantJoinResult(
            new WishlistParticipantDetails(
                Guid.CreateVersion7(),
                "Member Jenn"),
            true,
            null,
            null);
        using var client = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var response = await SendJoinAsync(
            client,
            Guid.CreateVersion7(),
            null,
            includeBody: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        Assert.DoesNotContain(
            response.Headers.TryGetValues(
                "Set-Cookie",
                out var cookies)
                ? cookies
                : [],
            value => value.StartsWith(
                $"{GuestSessionCookieService.LocalCookieName}=",
                StringComparison.Ordinal));
        var join = Assert.Single(factory.WishlistParticipantService.Joins);
        Assert.Equal(
            memberId,
            join.MemberId);
        Assert.Null(join.DisplayName);
    }

    [Fact]
    public async Task JoinAsync_WhenGuestAlreadyJoined_ReturnsOkAndReusesBrowserIdentity()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var shareLinkId = Guid.CreateVersion7();
        using var firstResponse = await SendJoinAsync(
            client,
            shareLinkId,
            "Guest Jenn",
            includeBody: true);
        var participant = factory.WishlistParticipantService.JoinResult.Participant;
        factory.WishlistParticipantService.JoinResult = new WishlistParticipantJoinResult(
            participant,
            false,
            null,
            null);

        // Act
        using var secondResponse = await SendJoinAsync(
            client,
            shareLinkId,
            "Renamed guest",
            includeBody: true);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            secondResponse.StatusCode);
        Assert.Equal(
            2,
            factory.WishlistParticipantService.Joins.Count);
        var secondJoin = factory.WishlistParticipantService.Joins[1];
        Assert.NotNull(secondJoin.GuestToken);
        Assert.StartsWith(
            "0198e75d828070008000000000000011.",
            secondJoin.GuestToken,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenGuestHasJoined_ReturnsCurrentParticipant()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Guest Jenn");
        factory.WishlistParticipantService.LookupResult = new WishlistParticipantLookupResult(
            WishlistParticipantLookupOutcome.Found,
            participant);
        using var client = factory.CreateClient();
        using var request = CreateSharedRequest(
            HttpMethod.Get,
            Guid.CreateVersion7(),
            "participants/current");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var actual = await response.Content.ReadFromJsonAsync<WishlistParticipantResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The participant response is empty.");
        Assert.Equal(
            participant.Id,
            actual.Id);
        Assert.Equal(
            participant.DisplayName,
            actual.DisplayName);
    }

    [Fact]
    public async Task GetSharedAsync_WhenCurrentParticipantExists_IncludesOnlyThatParticipant()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Guest Jenn");
        factory.WishlistParticipantService.LookupResult = new WishlistParticipantLookupResult(
            WishlistParticipantLookupOutcome.Found,
            participant);
        using var client = factory.CreateClient();
        using var request = CreateSharedRequest(
            HttpMethod.Get,
            Guid.CreateVersion7());

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The shared wishlist response is empty.");
        var currentParticipant = document.RootElement.GetProperty("currentParticipant");
        Assert.Equal(
            participant.Id,
            currentParticipant.GetProperty("id").GetGuid());
        Assert.Equal(
            participant.DisplayName,
            currentParticipant.GetProperty("displayName").GetString());
        Assert.False(document.RootElement.TryGetProperty(
            "participants",
            out _));
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Unauthorized, "GUEST_SESSION_INVALID", true)]
    [InlineData(false, HttpStatusCode.NotFound, "WISHLIST_PARTICIPANT_NOT_FOUND", false)]
    public async Task GetCurrentAsync_WhenParticipationIsUnavailable_ReturnsStructuredError(
        bool guestSessionIsInvalid,
        HttpStatusCode expectedStatus,
        string expectedErrorCode,
        bool expectsDeletedCookie)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistParticipantService.LookupResult = new WishlistParticipantLookupResult(
            guestSessionIsInvalid
                ? WishlistParticipantLookupOutcome.InvalidGuestSession
                : WishlistParticipantLookupOutcome.NotJoined,
            null);
        using var client = factory.CreateClient();
        using var request = CreateSharedRequest(
            HttpMethod.Get,
            Guid.CreateVersion7(),
            "participants/current");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            expectedErrorCode,
            document.RootElement.GetProperty("errorCode").GetString());
        var hasDeletedCookie = response.Headers.TryGetValues(
            "Set-Cookie",
            out var cookies) && cookies.Any(value =>
                value.StartsWith(
                    $"{GuestSessionCookieService.LocalCookieName}=",
                    StringComparison.Ordinal) &&
                value.Contains(
                    "expires=Thu, 01 Jan 1970",
                    StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            expectsDeletedCookie,
            hasDeletedCookie);
    }

    [Fact]
    public async Task JoinAsync_WhenGuestDisplayNameIsMissing_ReturnsValidationError()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await SendJoinAsync(
            client,
            Guid.CreateVersion7(),
            null,
            includeBody: true);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            "REQUEST_VALIDATION_ERROR",
            document.RootElement.GetProperty("errorCode").GetString());
        var validationError = Assert.Single(
            document.RootElement.GetProperty("validationErrors").EnumerateArray());
        Assert.Equal(
            "displayName",
            validationError.GetProperty("propertyName").GetString());
        Assert.Empty(factory.WishlistParticipantService.Joins);
    }

    [Fact]
    public async Task JoinAsync_WhenAntiforgeryTokenIsMissing_ReturnsBadRequest()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateSharedRequest(
            HttpMethod.Post,
            Guid.CreateVersion7(),
            "participants");
        request.Content = JsonContent.Create(new
        {
            displayName = "Guest Jenn"
        });

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.WishlistParticipantService.Joins);
    }

    [Theory]
    [InlineData(true, "WISHLIST_OWNER_CANNOT_JOIN")]
    [InlineData(false, "WISHLIST_PARTICIPANT_LIMIT_REACHED")]
    public async Task JoinAsync_WhenBusinessRuleRejectsJoin_ReturnsStructuredConflict(
        bool ownerIsJoining,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistParticipantService.JoinException = ownerIsJoining
            ? new WishlistOwnerCannotJoinException()
            : new WishlistParticipantLimitReachedException();
        using var client = factory.CreateClient();

        // Act
        using var response = await SendJoinAsync(
            client,
            Guid.CreateVersion7(),
            "Guest Jenn",
            includeBody: true);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            expectedErrorCode,
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task JoinAsync_WhenRateLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var shareLinkId = Guid.CreateVersion7();
        var csrfToken = await GetCsrfTokenAsync(client);
        HttpResponseMessage? response = null;

        try
        {
            // Act
            for (var requestNumber = 0;
                requestNumber <= AuthenticationRateLimitingExtensions.SharedWishlistJoinPermitLimit;
                requestNumber++)
            {
                response?.Dispose();
                response = await SendJoinAsync(
                    client,
                    shareLinkId,
                    "Guest Jenn",
                    includeBody: true,
                    csrfToken);
            }

            // Assert
            Assert.NotNull(response);
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
                TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The error response is empty.");
            Assert.Equal(
                "REQUEST_RATE_LIMIT_EXCEEDED",
                document.RootElement.GetProperty("errorCode").GetString());
        }
        finally
        {
            response?.Dispose();
        }
    }

    [Fact]
    public async Task JoinAsync_WhenProductionGuestIsNew_ReturnsSecureHostCookie()
    {
        // Arrange
        using var keys = new TemporaryKeyDirectory();
        await using var factory = new RegistrationApiFactory(
            environment: "Production",
            dataProtectionKeysPath: keys.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // Act
        using var response = await SendJoinAsync(
            client,
            Guid.CreateVersion7(),
            "Guest Jenn",
            includeBody: true);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                $"{GuestSessionCookieService.ProductionCookieName}=",
                StringComparison.Ordinal));
        Assert.Contains(
            "secure",
            cookie,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseMessage> SendJoinAsync(
        HttpClient client,
        Guid shareLinkId,
        string? displayName,
        bool includeBody,
        string? existingCsrfToken = null)
    {
        var csrfToken = existingCsrfToken ?? await GetCsrfTokenAsync(client);
        var request = CreateSharedRequest(
            HttpMethod.Post,
            shareLinkId,
            "participants");

        if (includeBody)
        {
            request.Content = JsonContent.Create(new
            {
                displayName
            });
        }

        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static HttpRequestMessage CreateSharedRequest(
        HttpMethod method,
        Guid shareLinkId,
        string? suffix = null)
    {
        var path = suffix is null
            ? $"/api/v1/shared-wishlists/{shareLinkId}"
            : $"/api/v1/shared-wishlists/{shareLinkId}/{suffix}";
        var request = new HttpRequestMessage(
            method,
            path);
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            ShareSecret);

        return request;
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);

        return response?.Token
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
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
}
