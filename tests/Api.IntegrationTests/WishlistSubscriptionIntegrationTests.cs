using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistSubscriptionIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task Subscription_WhenCreatedReadAndRemoved_PreservesParticipationIndependence()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(ct);
        var seed = await SeedAsync(
            factory,
            ct);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.MemberId,
            ct);

        // Act
        using var created = await SubscribeAsync(
            client,
            seed.ShareId,
            seed.Secret,
            ct);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = body.GetProperty("id").GetGuid();
        using var read = await client.GetAsync(
            created.Headers.Location,
            ct);
        using var current = await SendSharedAsync(
            client,
            HttpMethod.Get,
            $"{seed.ShareId}/subscriptions/current",
            seed.Secret,
            ct);
        using var page = await client.GetAsync(
            "/api/v1/wishlist-subscriptions",
            ct);
        using var participant = await SendSharedAsync(
            client,
            HttpMethod.Get,
            $"{seed.ShareId}/participants/current",
            seed.Secret,
            ct);
        using var deleted = await client.DeleteAsync(
            $"/api/v1/wishlist-subscriptions/{id}",
            ct);
        using var missing = await client.GetAsync(
            $"/api/v1/wishlist-subscriptions/{id}",
            ct);
        using var secondDelete = await client.DeleteAsync(
            $"/api/v1/wishlist-subscriptions/{id}",
            ct);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            created.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            read.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            current.StatusCode);
        Assert.Equal(
            seed.WishlistId,
            body.GetProperty("wishlistId").GetGuid());
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("eventDate").ValueKind);
        Assert.False(body.TryGetProperty(
            "protectedSecret",
            out _));
        Assert.False(body.TryGetProperty(
            "shareSecretHash",
            out _));
        var pageBody = await page.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Single(pageBody.GetProperty("items").EnumerateArray());
        Assert.Equal(
            HttpStatusCode.NotFound,
            participant.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            deleted.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            missing.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            secondDelete.StatusCode);
    }

    [Theory]
    [InlineData("rotate")]
    [InlineData("revoke")]
    [InlineData("archive")]
    [InlineData("suspend")]
    [InlineData("delete")]
    public async Task Subscription_WhenAccessWithdrawn_DisappearsAndNeverReturnsAutomatically(string action)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(ct);
        var seed = await SeedAsync(
            factory,
            ct);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.MemberId,
            ct);

        if (action == "suspend")
        {
            await WishlistModerationHttpTestHelper.GrantAdministratorAsync(
                factory,
                seed.OwnerId,
                ct);
        }

        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            ct);
        using var created = await SubscribeAsync(
            client,
            seed.ShareId,
            seed.Secret,
            ct);
        Assert.Equal(
            HttpStatusCode.Created,
            created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = body.GetProperty("id").GetGuid();

        // Act
        await WithdrawAccessAsync(
            factory,
            owner,
            seed.WishlistId,
            action,
            ct);
        using var read = await client.GetAsync(
            $"/api/v1/wishlist-subscriptions/{id}",
            ct);
        using var page = await client.GetAsync(
            "/api/v1/wishlist-subscriptions",
            ct);

        if (action is "archive" or "suspend")
        {
            await SetStateAsync(
                owner,
                seed.WishlistId,
                action,
                false,
                ct);
        }

        using var restored = await client.GetAsync(
            "/api/v1/wishlist-subscriptions",
            ct);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            read.StatusCode);
        var pageBody = await page.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Empty(pageBody.GetProperty("items").EnumerateArray());
        var restoredBody = await restored.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Empty(restoredBody.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Subscription_WhenConcurrentRequestsArrive_CreatesExactlyOne()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(ct);
        var seed = await SeedAsync(
            factory,
            ct);
        using var first = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.MemberId,
            ct);
        using var second = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.MemberId,
            ct);
        await SetCsrfAsync(
            first,
            ct);
        await SetCsrfAsync(
            second,
            ct);

        // Act
        var responses = await Task.WhenAll(
            SendSharedAsync(
                first,
                HttpMethod.Post,
                $"{seed.ShareId}/subscriptions",
                seed.Secret,
                ct),
            SendSharedAsync(
                second,
                HttpMethod.Post,
                $"{seed.ShareId}/subscriptions",
                seed.Secret,
                ct));
        using var page = await first.GetAsync(
            "/api/v1/wishlist-subscriptions",
            ct);

        // Assert
        Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.Conflict);
        var pageBody = await page.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Single(pageBody.GetProperty("items").EnumerateArray());
        foreach (var response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task Subscription_WhenForeignMemberOrOwnerOrInvalidSecret_EnforcesAccessRules()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(ct);
        var seed = await SeedAsync(
            factory,
            ct);
        using var member = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.MemberId,
            ct);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            ct);
        using var anonymous = factory.CreateClient();
        using var created = await SubscribeAsync(
            member,
            seed.ShareId,
            seed.Secret,
            ct);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = body.GetProperty("id").GetGuid();

        // Act
        using var foreign = await owner.GetAsync(
            $"/api/v1/wishlist-subscriptions/{id}",
            ct);
        using var foreignDelete = await owner.DeleteAsync(
            $"/api/v1/wishlist-subscriptions/{id}",
            ct);
        using var self = await SubscribeAsync(
            owner,
            seed.ShareId,
            seed.Secret,
            ct);
        using var invalid = await SubscribeAsync(
            member,
            seed.ShareId,
            new string(
                'A',
                43),
            ct);
        using var unauthenticated = await anonymous.GetAsync(
            "/api/v1/wishlist-subscriptions",
            ct);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            foreign.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            foreignDelete.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            self.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            invalid.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            unauthenticated.StatusCode);
    }

    [Theory]
    [InlineData("?page=0", HttpStatusCode.BadRequest)]
    [InlineData("?pageSize=101", HttpStatusCode.BadRequest)]
    [InlineData("?page=2147483647", HttpStatusCode.OK)]
    public async Task GetPageAsync_WhenPaginationIsSpecified_ValidatesWithoutOverflow(
        string query,
        HttpStatusCode expected)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(ct);
        var seed = await SeedAsync(
            factory,
            ct);
        using var member = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.MemberId,
            ct);

        // Act
        using var response = await member.GetAsync(
            $"/api/v1/wishlist-subscriptions{query}",
            ct);

        // Assert
        Assert.Equal(
            expected,
            response.StatusCode);
    }

    [Theory]
    [InlineData("rotate")]
    [InlineData("revoke")]
    [InlineData("archive")]
    public async Task Subscription_WhenAccessIsWithdrawnDuringCreation_DoesNotRetainAccess(string action)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            cancellationToken);
        using var member = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.MemberId,
            cancellationToken);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);
        await SetCsrfAsync(
            member,
            cancellationToken);

        // Act
        var creating = SendSharedAsync(
            member,
            HttpMethod.Post,
            $"{seed.ShareId}/subscriptions",
            seed.Secret,
            cancellationToken);
        var withdrawing = WithdrawAccessAsync(
            factory,
            owner,
            seed.WishlistId,
            action,
            cancellationToken);
        await Task.WhenAll(
            creating,
            withdrawing);
        using var created = await creating;
        using var page = await member.GetAsync(
            "/api/v1/wishlist-subscriptions",
            cancellationToken);
        var result = await page.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.True(created.StatusCode is HttpStatusCode.Created or HttpStatusCode.NotFound);
        Assert.Equal(
            0,
            result.GetProperty("totalCount").GetInt32());
        Assert.Empty(result.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetSharedAsync_WhenCallerOwnsList_DeclinesSubscriptionWithoutExposingOwnerIdentifier()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            cancellationToken);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);
        using var member = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.MemberId,
            cancellationToken);

        // Act
        using var own = await SendSharedAsync(
            owner,
            HttpMethod.Get,
            seed.ShareId.ToString(),
            seed.Secret,
            cancellationToken);
        using var other = await SendSharedAsync(
            member,
            HttpMethod.Get,
            seed.ShareId.ToString(),
            seed.Secret,
            cancellationToken);
        var owned = await own.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var shared = await other.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.False(owned.GetProperty("canSubscribe").GetBoolean());
        Assert.True(shared.GetProperty("canSubscribe").GetBoolean());
        Assert.False(owned.TryGetProperty(
            "ownerId",
            out _));
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(CancellationToken cancellationToken)
    {
        await fixture.ResetDatabaseAsync(cancellationToken);

        return new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
    }

    private static async Task<(Guid OwnerId, Guid MemberId, Guid WishlistId, Guid ShareId, string Secret)> SeedAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var owner = WishlistSubscriptionTestData.CreateMember();
        var member = WishlistSubscriptionTestData.CreateMember();
        var wishlistId = Guid.CreateVersion7();
        var shareId = Guid.CreateVersion7();
        var token = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>().Create();
        context.Users.AddRange(
            owner,
            member);
        context.Wishlists.Add(new Wishlist(
            wishlistId,
            owner.Id,
            "Shared list",
            "SHARED LIST",
            WishlistOccasion.Other,
            null,
            null));
        context.WishlistShareLinks.Add(new WishlistShareLink(
            shareId,
            wishlistId,
            token.SecretHash,
            token.ProtectedSecret));
        await context.SaveChangesAsync(cancellationToken);

        return (
            owner.Id,
            member.Id,
            wishlistId,
            shareId,
            token.Secret);
    }

    private static async Task<HttpResponseMessage> SubscribeAsync(
        HttpClient client,
        Guid shareId,
        string secret,
        CancellationToken cancellationToken)
    {
        await SetCsrfAsync(
            client,
            cancellationToken);

        return await SendSharedAsync(
            client,
            HttpMethod.Post,
            $"{shareId}/subscriptions",
            secret,
            cancellationToken);
    }

    private static async Task SetCsrfAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        var csrf = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Missing CSRF response.");
        client.DefaultRequestHeaders.Remove(WebSecurityOptions.AntiforgeryHeaderName);
        client.DefaultRequestHeaders.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrf.Token);
    }

    private static async Task<HttpResponseMessage> SendSharedAsync(
        HttpClient client,
        HttpMethod method,
        string suffix,
        string secret,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            $"/api/v1/shared-wishlists/{suffix}");
        request.Headers.Add(
            "X-MonKado-Share-Token",
            secret);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task WithdrawAccessAsync(
        PostgreSqlApiFactory factory,
        HttpClient owner,
        Guid wishlistId,
        string action,
        CancellationToken cancellationToken)
    {

        if (action is "rotate" or "revoke")
        {
            var route = $"/api/v1/wishlists/{wishlistId}/share-link";
            using var current = await owner.GetAsync(
                route,
                cancellationToken);
            using var mutation = new HttpRequestMessage(
                action == "rotate" ? HttpMethod.Put : HttpMethod.Delete,
                route);
            mutation.Headers.TryAddWithoutValidation(
                "If-Match",
                current.Headers.ETag?.Tag);
            using var response = await owner.SendAsync(
                mutation,
                cancellationToken);
            Assert.True(response.IsSuccessStatusCode);

            return;
        }

        await SetStateAsync(
            owner,
            wishlistId,
            action,
            true,
            cancellationToken);
    }

    private static async Task SetStateAsync(
        HttpClient owner,
        Guid wishlistId,
        string action,
        bool enabled,
        CancellationToken cancellationToken)
    {

        if (action == "suspend")
        {
            await WishlistModerationHttpTestHelper.SetStateAsync(
                owner,
                wishlistId,
                enabled,
                cancellationToken);

            return;
        }

        var route = $"/api/v1/wishlists/{wishlistId}";
        using var current = await owner.GetAsync(
            route,
            cancellationToken);
        using var mutation = new HttpRequestMessage(
            action == "delete" ? HttpMethod.Delete : HttpMethod.Patch,
            route)
        {
            Content = action == "delete" ? null : JsonContent.Create(new { isArchived = enabled })
        };
        mutation.Headers.TryAddWithoutValidation(
            "If-Match",
            current.Headers.ETag?.Tag);
        using var response = await owner.SendAsync(
            mutation,
            cancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }
}
