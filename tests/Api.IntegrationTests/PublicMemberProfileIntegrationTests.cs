using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class PublicMemberProfileIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task GetAsync_WhenListsHaveDifferentAccess_ReturnsOnlyActiveUnsuspendedOwnedLists()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(token);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var owner = CreateMember(true);
        var other = CreateMember(true);
        context.Users.AddRange(
            owner,
            other);
        var active = CreateList(owner.Id);
        var second = CreateList(owner.Id);
        var privateList = CreateList(owner.Id);
        var suspended = CreateList(owner.Id);
        suspended.Moderate(
            true,
            "Not publicly disclosed",
            DateTime.UnixEpoch);
        var foreign = CreateList(other.Id);
        context.Wishlists.AddRange(
            active,
            second,
            privateList,
            suspended,
            foreign);
        var tokens = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>();
        foreach (var list in new[]
        {
            active,
            second,
            suspended,
            foreign
        })
        {
            var share = tokens.Create();
            context.WishlistShareLinks.Add(new WishlistShareLink(
                Guid.CreateVersion7(),
                list.Id,
                share.SecretHash,
                share.ProtectedSecret));
        }
        await context.SaveChangesAsync(token);
        using var visitor = factory.CreateClient();

        // Act
        using var response = await visitor.GetAsync(
            $"/api/v1/members/{owner.Id}/profile",
            token);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(token);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            owner.Id,
            body.GetProperty("id")
                .GetGuid());
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("profileImageUrl").ValueKind);
        var expected = new[]
            {
                active,
                second
            }
            .OrderByDescending(list => list.CreatedAt)
            .ThenBy(list => list.Id)
            .Select(list => list.Id);
        Assert.Equal(
            expected,
            body.GetProperty("wishlists")
                .EnumerateArray()
                .Select(list => list.GetProperty("id")
                    .GetGuid()));
        foreach (var list in body.GetProperty("wishlists")
            .EnumerateArray())
        {
            var shareUrl = list.GetProperty("shareUrl")
                .GetString();
            Assert.NotNull(shareUrl);
            using var shared = await ReadSharedAsync(
                visitor,
                shareUrl,
                token);
            Assert.Equal(
                HttpStatusCode.OK,
                shared.StatusCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_WhenMemberIsUnconfirmedOrDeleted_ReturnsNotFound(bool deleted)
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(token);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = CreateMember(false);
        context.Users.Add(member);
        await context.SaveChangesAsync(token);

        if (deleted)
        {
            context.Users.Remove(member);
            await context.SaveChangesAsync(token);
        }
        using var visitor = factory.CreateClient();

        // Act
        using var response = await visitor.GetAsync(
            $"/api/v1/members/{member.Id}/profile",
            token);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(token);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        Assert.Equal(
            "ACCOUNT_PUBLIC_PROFILE_NOT_FOUND",
            body.GetProperty("errorCode")
                .GetString());
    }

    [Fact]
    public async Task GetAsync_WhenSharingChanges_UsesCurrentLinkAndImmediatelyRemovesRevokedLists()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(token);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = CreateMember(true);
        var list = CreateList(member.Id);
        context.Users.Add(member);
        context.Wishlists.Add(list);
        await context.SaveChangesAsync(token);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            token);
        using var visitor = factory.CreateClient();
        var profilePath = $"/api/v1/members/{member.Id}/profile";
        var sharePath = $"/api/v1/wishlists/{list.Id}/share-link";

        // Act
        var before = await visitor.GetFromJsonAsync<JsonElement>(
            profilePath,
            token);
        using var created = await owner.PostAsync(
            sharePath,
            null,
            token);
        Assert.Equal(
            HttpStatusCode.Created,
            created.StatusCode);
        var first = await visitor.GetFromJsonAsync<JsonElement>(
            profilePath,
            token);
        var firstUrl = Assert.Single(first.GetProperty("wishlists")
            .EnumerateArray())
            .GetProperty("shareUrl")
            .GetString();
        Assert.NotNull(firstUrl);
        using var firstAccess = await ReadSharedAsync(
            visitor,
            firstUrl,
            token);
        using var rotateRequest = new HttpRequestMessage(
            HttpMethod.Put,
            sharePath);
        Assert.NotNull(created.Headers.ETag);
        rotateRequest.Headers.IfMatch.Add(created.Headers.ETag);
        using var rotated = await owner.SendAsync(
            rotateRequest,
            token);
        var afterRotation = await visitor.GetFromJsonAsync<JsonElement>(
            profilePath,
            token);
        var nextUrl = Assert.Single(afterRotation.GetProperty("wishlists")
            .EnumerateArray())
            .GetProperty("shareUrl")
            .GetString();
        Assert.NotNull(nextUrl);
        using var oldAccess = await ReadSharedAsync(
            visitor,
            firstUrl,
            token);
        using var newAccess = await ReadSharedAsync(
            visitor,
            nextUrl,
            token);
        using var revokeRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            sharePath);
        Assert.NotNull(rotated.Headers.ETag);
        revokeRequest.Headers.IfMatch.Add(rotated.Headers.ETag);
        using var revoked = await owner.SendAsync(
            revokeRequest,
            token);
        var afterRevocation = await visitor.GetFromJsonAsync<JsonElement>(
            profilePath,
            token);
        using var revokedAccess = await ReadSharedAsync(
            visitor,
            nextUrl,
            token);

        // Assert
        Assert.Empty(before.GetProperty("wishlists")
            .EnumerateArray());
        Assert.Equal(
            HttpStatusCode.OK,
            firstAccess.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            rotated.StatusCode);
        Assert.NotEqual(
            firstUrl,
            nextUrl);
        Assert.Equal(
            HttpStatusCode.NotFound,
            oldAccess.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            newAccess.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            revoked.StatusCode);
        Assert.Empty(afterRevocation.GetProperty("wishlists")
            .EnumerateArray());
        Assert.Equal(
            HttpStatusCode.NotFound,
            revokedAccess.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAsync_WhenListIsSuspendedOrDeleted_RemovesItFromNextRead(bool suspended)
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(token);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = CreateMember(true);
        var list = CreateList(member.Id);
        var share = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>()
            .Create();
        context.Users.Add(member);
        context.Wishlists.Add(list);
        context.WishlistShareLinks.Add(new WishlistShareLink(
            Guid.CreateVersion7(),
            list.Id,
            share.SecretHash,
            share.ProtectedSecret));
        await context.SaveChangesAsync(token);
        using var visitor = factory.CreateClient();
        var path = $"/api/v1/members/{member.Id}/profile";
        var before = await visitor.GetFromJsonAsync<JsonElement>(
            path,
            token);

        // Act
        if (suspended)
        {
            list.Moderate(
                true,
                "Test moderation",
                DateTime.UnixEpoch);
        }
        else
        {
            context.Wishlists.Remove(list);
        }
        await context.SaveChangesAsync(token);
        var after = await visitor.GetFromJsonAsync<JsonElement>(
            path,
            token);

        // Assert
        Assert.Single(before.GetProperty("wishlists")
            .EnumerateArray());
        Assert.Empty(after.GetProperty("wishlists")
            .EnumerateArray());
    }

    [Fact]
    public async Task GetAsync_WhenPhotoChanges_ReturnsCurrentPublicPhotoReference()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(token);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = CreateMember(true);
        context.Users.Add(member);
        await context.SaveChangesAsync(token);
        using var visitor = factory.CreateClient();
        var imageId = Guid.CreateVersion7();
        var path = $"/api/v1/members/{member.Id}/profile";

        // Act
        member.SetProfileImage(
            imageId,
            new byte[32]);
        await context.SaveChangesAsync(token);
        var withPhoto = await visitor.GetFromJsonAsync<JsonElement>(
            path,
            token);
        member.RemoveProfileImage();
        await context.SaveChangesAsync(token);
        var withoutPhoto = await visitor.GetFromJsonAsync<JsonElement>(
            path,
            token);

        // Assert
        Assert.Contains(
            $"/api/v1/members/{member.Id}/profile/image?imageId={imageId}",
            withPhoto.GetProperty("profileImageUrl")
                .GetString());
        Assert.Equal(
            JsonValueKind.Null,
            withoutPhoto.GetProperty("profileImageUrl").ValueKind);
    }

    private static MonKadoUser CreateMember(bool confirmed)
    {
        var id = Guid.CreateVersion7();
        var email = $"profile-{id:N}@example.test";

        return new MonKadoUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = confirmed,
            DisplayName = "Same public name",
            SecurityStamp = Guid.CreateVersion7().ToString()
        };
    }

    private static Wishlist CreateList(Guid ownerId)
    {
        var id = Guid.CreateVersion7();

        return new Wishlist(
            id,
            ownerId,
            $"List {id}",
            $"LIST {id}",
            WishlistOccasion.Birthday,
            null,
            null);
    }

    private static async Task<HttpResponseMessage> ReadSharedAsync(
        HttpClient visitor,
        string shareUrl,
        CancellationToken cancellationToken)
    {
        var url = new Uri(shareUrl);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1" + url.AbsolutePath);
        request.Headers.Add(
            "X-MonKado-Share-Token",
            url.Fragment[1..]);

        return await visitor.SendAsync(
            request,
            cancellationToken);
    }
}
