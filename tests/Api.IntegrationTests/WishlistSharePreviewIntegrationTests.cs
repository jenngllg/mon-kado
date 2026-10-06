using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistSharePreviewIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task GetAsync_WhenNoImages_ReturnsTitleOnlyAndImageNotFound()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(token);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var list = CreateList(context);
        var secret = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>().Create();
        var link = new WishlistShareLink(
            Guid.CreateVersion7(),
            list.Id,
            secret.SecretHash,
            secret.ProtectedSecret);
        context.WishlistShareLinks.Add(link);
        await context.SaveChangesAsync(token);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/shared-wishlists/{link.Id}/preview",
            token);
        var html = await response.Content.ReadAsStringAsync(token);
        using var image = await client.GetAsync(
            $"/api/v1/shared-wishlists/{link.Id}/preview/image",
            token);
        using var protectedList = await client.GetAsync(
            $"/api/v1/shared-wishlists/{link.Id}",
            token);
        using var authorizedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{link.Id}");
        authorizedRequest.Headers.Add(
            "X-MonKado-Share-Token",
            secret.Secret);
        using var authorizedList = await client.SendAsync(
            authorizedRequest,
            token);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Contains("og:title", html);
        Assert.Contains(
            $"<meta property=\"og:url\" content=\"http://localhost:5173/shared-wishlists/{link.Id:D}\">",
            html);
        Assert.DoesNotContain(
            secret.Secret,
            html);
        Assert.DoesNotContain("og:image", html);
        Assert.Equal(
            HttpStatusCode.NotFound,
            image.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            protectedList.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            authorizedList.StatusCode);
    }

    [Fact]
    public async Task GetImageAsync_WhenArchivedThenRestoredAndRevoked_EnforcesCurrentPublicAccess()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(token);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var list = CreateList(context);
        var secret = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>().Create();
        var link = new WishlistShareLink(
            Guid.CreateVersion7(),
            list.Id,
            secret.SecretHash,
            secret.ProtectedSecret);
        context.WishlistShareLinks.Add(link);
        var wish = CreateWish(
            list.Id,
            1);
        context.Wishes.Add(wish);
        var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
        using var bitmap = new SKBitmap(
            80,
            160);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var content = image.Encode(
            SKEncodedImageFormat.Webp,
            100);
        await store.WritePendingAsync(
            wish.ImageId.GetValueOrDefault(),
            content.ToArray(),
            token);
        await context.SaveChangesAsync(token);
        using var visitor = factory.CreateClient();
        var path = $"/api/v1/shared-wishlists/{link.Id}/preview/image";

        // Act
        using var active = await visitor.GetAsync(
            path,
            token);
        var bytes = await active.Content.ReadAsByteArrayAsync(token);
        list.SetArchived(true);
        await context.SaveChangesAsync(token);
        using var archived = await visitor.GetAsync(
            path,
            token);
        list.SetArchived(false);
        await context.SaveChangesAsync(token);
        using var restored = await visitor.GetAsync(
            path,
            token);
        context.WishlistShareLinks.Remove(link);
        await context.SaveChangesAsync(token);
        using var revoked = await visitor.GetAsync(
            path,
            token);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            active.StatusCode);
        Assert.True(active.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "image/jpeg",
            active.Content.Headers.ContentType?.MediaType);
        using var decoded = SKBitmap.Decode(bytes);
        Assert.Equal(
            1200,
            decoded.Width);
        Assert.Equal(
            630,
            decoded.Height);
        Assert.Equal(
            HttpStatusCode.NotFound,
            archived.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            restored.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            revoked.StatusCode);
    }

    [Fact]
    public async Task GetAsync_WhenActive_ReturnsEscapedTitleAndOnlyTwoImagesWithoutPrivateData()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(token);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var list = CreateList(context);
        var secret = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>().Create();
        var link = new WishlistShareLink(
            Guid.CreateVersion7(),
            list.Id,
            secret.SecretHash,
            secret.ProtectedSecret);
        context.WishlistShareLinks.Add(link);
        var wishes = Enumerable.Range(
                1,
                3)
            .Select(position => CreateWish(
                list.Id,
                position))
            .ToArray();
        context.Wishes.AddRange(wishes);
        var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();

        for (var index = 0; index < wishes.Length; index++)
        {
            using var bitmap = new SKBitmap(
                80,
                160);
            var colors = new[]
            {
                SKColors.Red,
                SKColors.Blue,
                SKColors.Green
            };
            bitmap.Erase(colors[index]);
            using var source = SKImage.FromBitmap(bitmap);
            using var content = source.Encode(
                SKEncodedImageFormat.Webp,
                100);
            await store.WritePendingAsync(
                wishes[index].ImageId.GetValueOrDefault(),
                content.ToArray(),
                token);
        }
        await context.SaveChangesAsync(token);
        using var visitor = factory.CreateClient();

        // Act
        using var response = await visitor.GetAsync(
            $"/api/v1/shared-wishlists/{link.Id}/preview",
            token);
        var html = await response.Content.ReadAsStringAsync(token);
        using var collage = await visitor.GetAsync(
            $"/api/v1/shared-wishlists/{link.Id}/preview/image",
            token);
        var bytes = await collage.Content.ReadAsByteArrayAsync(token);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "text/html",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("og:title", html);
        Assert.Contains(
            $"<meta property=\"og:url\" content=\"http://localhost:5173/shared-wishlists/{link.Id:D}\">",
            html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains($"/share-previews/{link.Id}/image", html);
        Assert.DoesNotContain(wishes[0].Id.ToString(), html);
        Assert.DoesNotContain(wishes[1].Id.ToString(), html);
        Assert.DoesNotContain(wishes[2].Id.ToString(), html);
        Assert.DoesNotContain("Private list message", html);
        Assert.DoesNotContain("Private wish note", html);
        Assert.DoesNotContain(secret.Secret, html);
        Assert.DoesNotContain("reserved", html);
        Assert.Equal(
            HttpStatusCode.OK,
            collage.StatusCode);
        using var decoded = SKBitmap.Decode(bytes);
        var left = decoded.GetPixel(
            300,
            315);
        var right = decoded.GetPixel(
            900,
            315);
        Assert.True(left.Red > 240 && left.Blue < 15);
        Assert.True(right.Blue > 240 && right.Red < 15);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("archived")]
    [InlineData("suspended")]
    [InlineData("unconfirmed")]
    [InlineData("revoked")]
    public async Task GetAsync_WhenSharingUnavailable_ReturnsNotFoundForHtmlAndImages(string state)
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(token);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var list = CreateList(context);
        list.SetArchived(state == "archived");

        if (state == "suspended")
            list.Moderate(
                true,
                "Not public",
                DateTime.UnixEpoch);

        if (state == "unconfirmed")
            context.Users.Local.Single().EmailConfirmed = false;
        var secret = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>().Create();
        var link = new WishlistShareLink(
            Guid.CreateVersion7(),
            list.Id,
            secret.SecretHash,
            secret.ProtectedSecret);

        if (state is not "private" and not "revoked")
            context.WishlistShareLinks.Add(link);
        await context.SaveChangesAsync(token);
        using var visitor = factory.CreateClient();

        // Act
        using var html = await visitor.GetAsync(
            $"/api/v1/shared-wishlists/{link.Id}/preview",
            token);
        using var image = await visitor.GetAsync(
            $"/api/v1/shared-wishlists/{link.Id}/preview/image",
            token);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            html.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            image.StatusCode);
    }

    private static Wishlist CreateList(MonKadoDbContext context)
    {
        var user = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "preview@example.test",
            NormalizedUserName = "PREVIEW@EXAMPLE.TEST",
            Email = "preview@example.test",
            NormalizedEmail = "PREVIEW@EXAMPLE.TEST",
            EmailConfirmed = true,
            DisplayName = "Preview owner",
            SecurityStamp = Guid.CreateVersion7().ToString()
        };
        context.Users.Add(user);
        var list = new Wishlist(
            Guid.CreateVersion7(),
            user.Id,
            "Noël <script>",
            "NOËL <SCRIPT>",
            WishlistOccasion.Birthday,
            null,
            "Private list message");
        context.Wishlists.Add(list);

        return list;
    }

    private static Wish CreateWish(
        Guid wishlistId,
        long position)
    {
        var wish = new Wish(
            Guid.CreateVersion7(),
            wishlistId,
            "Product",
            "Private wish note",
            null,
            null,
            position);
        wish.ReplaceImage(
            Guid.CreateVersion7(),
            new byte[32]);

        return wish;
    }
}
