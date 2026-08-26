using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistShareLinkIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task ShareLink_WhenCreatedRotatedAndRevoked_ProtectsSecretAndControlsPublicAccess()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        await SeedWishlistAsync(
            factory,
            ownerId,
            wishlistId,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var creation = await ownerClient.PostAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            null,
            cancellationToken);
        var createdBody = await creation.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken);
        var shareLinkId = createdBody.GetProperty("id").GetGuid();
        var firstSecret = GetSecret(createdBody);
        var stored = await GetStoredShareLinkAsync(
            factory,
            shareLinkId,
            cancellationToken);
        using var duplicateCreation = await ownerClient.PostAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            null,
            cancellationToken);
        using var ownerRetrieval = await ownerClient.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            cancellationToken);
        var ownerRetrievalBody = await ownerRetrieval.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken);
        using var firstPublicRead = await GetSharedWishlistAsync(
            factory,
            shareLinkId,
            firstSecret,
            cancellationToken);
        using var rotation = await RotateAsync(
            ownerClient,
            wishlistId,
            creation.Headers.ETag?.Tag,
            cancellationToken);
        var rotatedBody = await rotation.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken);
        var secondSecret = GetSecret(rotatedBody);
        using var stalePublicRead = await GetSharedWishlistAsync(
            factory,
            shareLinkId,
            firstSecret,
            cancellationToken);
        using var secondPublicRead = await GetSharedWishlistAsync(
            factory,
            shareLinkId,
            secondSecret,
            cancellationToken);
        using var staleRevocation = await DeleteAsync(
            ownerClient,
            wishlistId,
            creation.Headers.ETag?.Tag,
            cancellationToken);
        using var revocation = await DeleteAsync(
            ownerClient,
            wishlistId,
            rotation.Headers.ETag?.Tag,
            cancellationToken);
        using var revokedPublicRead = await GetSharedWishlistAsync(
            factory,
            shareLinkId,
            secondSecret,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            creation.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            duplicateCreation.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            ownerRetrieval.StatusCode);
        Assert.Equal(
            firstSecret,
            GetSecret(ownerRetrievalBody));
        Assert.Equal(
            32,
            stored.SecretHash.Length);
        Assert.DoesNotContain(
            firstSecret,
            Encoding.UTF8.GetString(stored.SecretHash),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            firstSecret,
            stored.ProtectedSecret,
            StringComparison.Ordinal);
        Assert.Equal(
            HttpStatusCode.OK,
            firstPublicRead.StatusCode);
        var publicBody = await firstPublicRead.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken);
        Assert.Equal(
            "Jenn",
            publicBody.GetProperty("ownerDisplayName").GetString());
        Assert.Equal(
            "Livre",
            publicBody.GetProperty("wishes")[0].GetProperty("name").GetString());
        Assert.NotEqual(
            firstSecret,
            secondSecret);
        Assert.Equal(
            HttpStatusCode.NotFound,
            stalePublicRead.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            secondPublicRead.StatusCode);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            staleRevocation.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            revocation.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            revokedPublicRead.StatusCode);
    }

    [Fact]
    public async Task Participant_WhenGuestJoinsAndShareLinkRotates_PreservesIdentityUntilGuestSessionExpires()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        await using var factory = await CreateFactoryAsync(
            cancellationToken,
            timeProvider);
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        await SeedWishlistAsync(
            factory,
            ownerId,
            wishlistId,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        using var creation = await ownerClient.PostAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            null,
            cancellationToken);
        var createdBody = await creation.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var shareLinkId = createdBody.GetProperty("id").GetGuid();
        var firstSecret = GetSecret(createdBody);
        using var guestClient = factory.CreateClient();

        // Act
        using var firstJoin = await JoinAsGuestAsync(
            guestClient,
            shareLinkId,
            firstSecret,
            "  Guest Jenn  ",
            cancellationToken);
        var firstJoinBody = await firstJoin.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(
            firstJoin.StatusCode == HttpStatusCode.Created,
            $"Unexpected join response {firstJoin.StatusCode}: {firstJoinBody}");
        using var firstParticipantDocument = JsonDocument.Parse(firstJoinBody);
        var firstParticipant = firstParticipantDocument.RootElement;
        var participantId = firstParticipant.GetProperty("id").GetGuid();
        var guestCookie = firstJoin.Headers.GetValues("Set-Cookie")
            .Select(GetCookiePair)
            .Single(value => value.StartsWith(
                $"{GuestSessionCookieService.LocalCookieName}=",
                StringComparison.Ordinal));
        var guestToken = guestCookie[(guestCookie.IndexOf('=') + 1)..];
        var sessionId = Guid.ParseExact(
            guestToken[..guestToken.IndexOf('.')],
            "N");
        using var repeatedJoin = await JoinAsGuestAsync(
            guestClient,
            shareLinkId,
            firstSecret,
            "Ignored rename",
            cancellationToken);
        var repeatedParticipant = await repeatedJoin.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken);
        using var rotation = await RotateAsync(
            ownerClient,
            wishlistId,
            creation.Headers.ETag?.Tag,
            cancellationToken);
        var rotatedBody = await rotation.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var secondSecret = GetSecret(rotatedBody);
        using var sharedRead = await GetSharedWishlistWithClientAsync(
            guestClient,
            shareLinkId,
            secondSecret,
            cancellationToken);
        var sharedBody = await sharedRead.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var storedIdentity = await GetStoredGuestIdentityAsync(
            factory,
            participantId,
            cancellationToken);
        timeProvider.Advance(TimeSpan.FromDays(181));
        var deletedSessions = await DeleteExpiredGuestSessionsAsync(
            factory,
            cancellationToken);
        var retainedParticipant = await GetStoredParticipantAsync(
            factory,
            participantId,
            cancellationToken);
        using var expiredCurrent = await GetCurrentParticipantAsync(
            guestClient,
            shareLinkId,
            secondSecret,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            firstJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            repeatedJoin.StatusCode);
        Assert.Equal(
            participantId,
            repeatedParticipant.GetProperty("id").GetGuid());
        Assert.Equal(
            HttpStatusCode.OK,
            rotation.StatusCode);
        Assert.NotEqual(
            firstSecret,
            secondSecret);
        Assert.Equal(
            HttpStatusCode.OK,
            sharedRead.StatusCode);
        Assert.Equal(
            participantId,
            sharedBody
                .GetProperty("currentParticipant")
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            sessionId,
            storedIdentity.Session.Id);
        Assert.Equal(
            32,
            storedIdentity.Session.SecretHash.Length);
        Assert.Equal(
            participantId,
            storedIdentity.Participant.Id);
        Assert.Equal(
            "Guest Jenn",
            storedIdentity.Participant.GuestDisplayName);
        Assert.Single(storedIdentity.AllParticipants);
        Assert.Equal(
            1,
            deletedSessions);
        Assert.Null(retainedParticipant.GuestSessionId);
        Assert.Equal(
            "Guest Jenn",
            retainedParticipant.GuestDisplayName);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            expiredCurrent.StatusCode);
        Assert.Contains(
            expiredCurrent.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                $"{GuestSessionCookieService.LocalCookieName}=",
                StringComparison.Ordinal) && value.Contains(
                    "expires=Thu, 01 Jan 1970",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Participant_WhenGuestBecomesMember_AttachesAndMergesBrowserParticipation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        await SeedWishlistAsync(
            factory,
            ownerId,
            wishlistId,
            cancellationToken);
        await SeedMemberAsync(
            factory,
            memberId,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        using var creation = await ownerClient.PostAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            null,
            cancellationToken);
        var createdBody = await creation.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var shareLinkId = createdBody.GetProperty("id").GetGuid();
        var secret = GetSecret(createdBody);
        using var guestClient = factory.CreateClient();
        using var guestJoin = await JoinAsGuestAsync(
            guestClient,
            shareLinkId,
            secret,
            "Guest before login",
            cancellationToken);
        var guestBody = await guestJoin.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var originalParticipantId = guestBody.GetProperty("id").GetGuid();
        var guestCookie = guestJoin.Headers.GetValues("Set-Cookie")
            .Select(GetCookiePair)
            .Single(value => value.StartsWith(
                $"{GuestSessionCookieService.LocalCookieName}=",
                StringComparison.Ordinal));
        using var memberClient = CreateAuthorizedClient(
            factory,
            memberId,
            handleCookies: false);
        var csrf = await GetCsrfExchangeAsync(
            memberClient,
            cancellationToken);

        // Act
        using var attachment = await JoinAsMemberAsync(
            memberClient,
            shareLinkId,
            secret,
            csrf,
            guestCookie,
            cancellationToken);
        using var duplicateGuestJoin = await JoinAsGuestAsync(
            guestClient,
            shareLinkId,
            secret,
            "Guest after logout",
            cancellationToken);
        var duplicateGuestBody = await duplicateGuestJoin.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken);
        using var merge = await JoinAsMemberAsync(
            memberClient,
            shareLinkId,
            secret,
            csrf,
            guestCookie,
            cancellationToken);
        var storedParticipants = await GetStoredParticipantsAsync(
            factory,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            attachment.StatusCode);
        var attachmentBody = await attachment.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(
            originalParticipantId,
            attachmentBody.GetProperty("id").GetGuid());
        Assert.Equal(
            "Member Jenn",
            attachmentBody.GetProperty("displayName").GetString());
        Assert.Equal(
            HttpStatusCode.Created,
            duplicateGuestJoin.StatusCode);
        Assert.NotEqual(
            originalParticipantId,
            duplicateGuestBody.GetProperty("id").GetGuid());
        Assert.Equal(
            HttpStatusCode.OK,
            merge.StatusCode);
        var participant = Assert.Single(storedParticipants);
        Assert.Equal(
            originalParticipantId,
            participant.Id);
        Assert.Equal(
            memberId,
            participant.MemberId);
        Assert.Null(participant.GuestSessionId);
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(
        CancellationToken cancellationToken,
        TimeProvider? timeProvider = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await fixture.ResetDatabaseAsync(cancellationToken);

        return factory;
    }

    private static async Task<(
        GuestSession Session,
        WishlistParticipant Participant,
        IReadOnlyCollection<WishlistParticipant> AllParticipants)> GetStoredGuestIdentityAsync(
        PostgreSqlApiFactory factory,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var participant = await context.WishlistParticipants
            .AsNoTracking()
            .SingleAsync(
                participant => participant.Id == participantId,
                cancellationToken);
        var session = await context.GuestSessions
            .AsNoTracking()
            .SingleAsync(
                session => session.Id == participant.GuestSessionId,
                cancellationToken);
        var allParticipants = await context.WishlistParticipants
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return (
            session,
            participant,
            allParticipants);
    }

    private static async Task<WishlistParticipant> GetStoredParticipantAsync(
        PostgreSqlApiFactory factory,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.WishlistParticipants
            .AsNoTracking()
            .SingleAsync(
                participant => participant.Id == participantId,
                cancellationToken);
    }

    private static async Task<int> DeleteExpiredGuestSessionsAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IExpiredGuestSessionCleanup>();

        return await cleanup.DeleteExpiredSessionsAsync(
            100,
            cancellationToken);
    }

    private static async Task SeedWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.Users.Add(new MonKadoUser
        {
            Id = ownerId,
            UserName = "owner@example.test",
            NormalizedUserName = "OWNER@EXAMPLE.TEST",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
            EmailConfirmed = true,
            DisplayName = "Jenn",
            SecurityStamp = Guid.CreateVersion7().ToString()
        });
        context.Wishlists.Add(new Wishlist(
            wishlistId,
            ownerId,
            "Anniversaire",
            "ANNIVERSAIRE",
            WishlistOccasion.Birthday,
            new DateOnly(
                2026,
                9,
                23),
            "Merci"));
        context.Wishes.Add(new Wish(
            Guid.CreateVersion7(),
            wishlistId,
            "Livre",
            "Note privée",
            "https://example.test/book",
            19.99m,
            1));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedMemberAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.Users.Add(new MonKadoUser
        {
            Id = memberId,
            UserName = "member@example.test",
            NormalizedUserName = "MEMBER@EXAMPLE.TEST",
            Email = "member@example.test",
            NormalizedEmail = "MEMBER@EXAMPLE.TEST",
            EmailConfirmed = true,
            DisplayName = "Member Jenn",
            SecurityStamp = Guid.CreateVersion7().ToString()
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private static HttpClient CreateAuthorizedClient(
        PostgreSqlApiFactory factory,
        Guid ownerId,
        bool handleCookies = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = handleCookies
        });
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(ownerId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
    }

    private static async Task<IReadOnlyCollection<WishlistParticipant>> GetStoredParticipantsAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.WishlistParticipants
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<WishlistShareLink> GetStoredShareLinkAsync(
        PostgreSqlApiFactory factory,
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.WishlistShareLinks
            .AsNoTracking()
            .SingleAsync(
                shareLink => shareLink.Id == shareLinkId,
                cancellationToken);
    }

    private static async Task<HttpResponseMessage> GetSharedWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            secret);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> GetSharedWishlistWithClientAsync(
        HttpClient client,
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            secret);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> GetCurrentParticipantAsync(
        HttpClient client,
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}/participants/current");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            secret);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> JoinAsGuestAsync(
        HttpClient client,
        Guid shareLinkId,
        string secret,
        string displayName,
        CancellationToken cancellationToken)
    {
        var csrfToken = await GetCsrfTokenAsync(
            client,
            cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/shared-wishlists/{shareLinkId}/participants")
        {
            Content = JsonContent.Create(new
            {
                displayName
            })
        };
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            secret);
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> JoinAsMemberAsync(
        HttpClient client,
        Guid shareLinkId,
        string secret,
        CsrfExchange csrf,
        string guestCookie,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/shared-wishlists/{shareLinkId}/participants");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            secret);
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrf.Token);
        request.Headers.Add(
            "Cookie",
            string.Concat(
                csrf.Cookie,
                "; ",
                guestCookie));

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");

        return payload.Token;
    }

    private static async Task<CsrfExchange> GetCsrfExchangeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(GetCookiePair)
            .Single(value => value.StartsWith(
                "MonKado.Antiforgery=",
                StringComparison.Ordinal));

        return new CsrfExchange(
            client,
            payload.Token,
            cookie);
    }

    private static async Task<HttpResponseMessage> RotateAsync(
        HttpClient client,
        Guid wishlistId,
        string? entityTag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/share-link");
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(
        HttpClient client,
        Guid wishlistId,
        string? entityTag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlistId}/share-link");
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static string GetSecret(JsonElement response)
    {
        var url = response.GetProperty("shareUrl").GetString()
            ?? throw new InvalidOperationException("The share URL is missing.");

        return url[(url.LastIndexOf('.') + 1)..];
    }

    private static string GetCookiePair(string setCookieHeader)
    {
        return setCookieHeader.Split(
            ';',
            2)[0];
    }
}
