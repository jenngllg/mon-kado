using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Authentication.JwtBearer;
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

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(CancellationToken cancellationToken)
    {
        var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await fixture.ResetDatabaseAsync(cancellationToken);

        return factory;
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

    private static HttpClient CreateAuthorizedClient(
        PostgreSqlApiFactory factory,
        Guid ownerId)
    {
        var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(ownerId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
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
}
