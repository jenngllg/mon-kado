using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SkiaSharp;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class ReportedWishlistImageIntegrationTests(PostgreSqlContainerFixture fixture) : IAsyncLifetime
{
    private readonly string _storagePath = Directory
        .CreateTempSubdirectory("mon-kado-reported-images-")
        .FullName;
    public ValueTask InitializeAsync()
    {

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(
            _storagePath,
            recursive: true);
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_WhenShareIsRevokedOrListIsSuspended_ExposesOnlyCurrentContentAndAuthenticatedImages(bool suspended)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        using var logs = new CapturingGoogleLoggerProvider();
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.AddLogging(logging => logging.AddProvider(logs)),
            giftImageStoragePath: _storagePath);
        var ownerId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            ct);
        var adminId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            ct);
        var wishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            ct);
        var otherList = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            ct);
        await ReportedWishlistTestData.AddReportAsync(
            factory,
            wishlist.Id,
            WishlistReportReason.Other,
            ct);
        await ReportedWishlistTestData.AddReportAsync(
            factory,
            otherList.Id,
            WishlistReportReason.Other,
            ct);
        var wish = new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "Private gift name",
            "Private gift note",
            "https://merchant.example.test/secret",
            12.50m,
            2,
            3);
        var noImage = new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "First gift",
            null,
            null,
            null,
            1);
        var firstImageId = Guid.CreateVersion7();
        var shareToken = factory.Services.GetRequiredService<IWishlistShareTokenService>().Create();
        var shareLink = new WishlistShareLink(
            Guid.CreateVersion7(),
            wishlist.Id,
            shareToken.SecretHash,
            shareToken.ProtectedSecret);
        var bytes = CreateWebP(SKColors.Red);
        wish.ReplaceImage(
            firstImageId,
            SHA256.HashData(bytes));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var tracked = await db.Wishlists.SingleAsync(
                item => item.Id == wishlist.Id,
                ct);

            if (suspended)
                tracked.Moderate(
                    true,
                    "Private suspension reason",
                    DateTime.UtcNow);
            db.Wishes.AddRange(
                wish,
                noImage);
            db.WishlistShareLinks.Add(shareLink);
            await db.SaveChangesAsync(ct);

            if (!suspended)
            {
                db.WishlistShareLinks.Remove(shareLink);
                await db.SaveChangesAsync(ct);
            }
            var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
            await store.WritePendingAsync(
                firstImageId,
                bytes,
                ct);
            await store.MarkCommittedAsync(
                firstImageId,
                ct);
        }

        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        using var owner = ReportedWishlistTestData.CreateClient(
            factory,
            ownerId);
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add(
            "X-MonKado-Share-Token",
            shareToken.Secret);
        var route = $"/api/v1/admin/reported-wishlists/{wishlist.Id}";

        // Act
        using var publicRead = await anonymous.GetAsync(
            $"/api/v1/shared-wishlists/{shareLink.Id}",
            ct);
        using var response = await admin.GetAsync(
            route,
            ct);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
        Assert.NotNull(document);
        var root = document.RootElement;
        var gifts = root.GetProperty("wishes");
        var imageUrl = gifts[1]
            .GetProperty("imageUrl")
            .GetString();
        Assert.NotNull(imageUrl);
        using var image = await admin.GetAsync(
            imageUrl,
            ct);
        using var noBearer = await anonymous.GetAsync(
            imageUrl,
            ct);
        using var deniedOwner = await owner.GetAsync(
            imageUrl,
            ct);
        using var wrongList = await admin.GetAsync(
            $"/api/v1/admin/reported-wishlists/{otherList.Id}/wishes/{wish.Id}/image",
            ct);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            publicRead.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            suspended,
            root
                .GetProperty("isSuspended")
                .GetBoolean());
        Assert.Equal(
            ownerId,
            root
                .GetProperty("ownerId")
                .GetGuid());
        Assert.Equal(
            "Private owner name",
            root
                .GetProperty("ownerDisplayName")
                .GetString());
        Assert.Equal(
            suspended ? "Private suspension reason" : null,
            root
                .GetProperty("suspensionReason")
                .GetString());
        Assert.Equal(
            [
                "createdAt",
                "eventDate",
                "isSuspended",
                "message",
                "name",
                "occasion",
                "ownerDisplayName",
                "ownerId",
                "suspendedAt",
                "suspensionReason",
                "updatedAt",
                "wishes",
                "wishlistId"
            ],
            root
                .EnumerateObject()
                .Select(property => property.Name)
                .Order());
        Assert.Equal(
            noImage.Id,
            gifts[0]
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            JsonValueKind.Null,
            gifts[0]
                .GetProperty("imageUrl")
                .ValueKind);
        Assert.Equal(
            [
                "createdAt",
                "id",
                "imageUrl",
                "name",
                "note",
                "position",
                "price",
                "quantity",
                "updatedAt",
                "url"
            ],
            gifts[1]
                .EnumerateObject()
                .Select(property => property.Name)
                .Order());
        Assert.Equal(
            3,
            gifts[1]
                .GetProperty("quantity")
                .GetInt32());
        Assert.Equal(
            12.50m,
            gifts[1]
                .GetProperty("price")
                .GetDecimal());
        Assert.Equal(
            "Private gift note",
            gifts[1]
                .GetProperty("note")
                .GetString());
        Assert.Empty(new Uri(imageUrl).Query);
        Assert.Equal(
            HttpStatusCode.OK,
            image.StatusCode);
        Assert.Equal(
            "image/webp",
            image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "nosniff",
            Assert.Single(image.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(image.Headers.CacheControl?.NoStore);
        Assert.Equal(
            bytes,
            await image.Content.ReadAsByteArrayAsync(ct));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            noBearer.StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            deniedOwner.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            wrongList.StatusCode);
        Assert.Contains(
            logs.Entries,
            entry => entry.Key == LogEventIds.ReportedWishlistRetrieved);
        Assert.Contains(
            logs.Entries,
            entry => entry.Key == LogEventIds.ReportedWishImageRetrieved);
        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Value.Contains(
                "Private",
                StringComparison.Ordinal) || entry.Value.Contains(
                "merchant.example.test",
                StringComparison.Ordinal) || entry.Value.Contains(
                _storagePath,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetImageAsync_WhenReferenceChangesOrFileDisappears_RevalidatesCurrentDatabaseState()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            giftImageStoragePath: _storagePath);
        var adminId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            ct);
        var wishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            adminId,
            ct);
        await ReportedWishlistTestData.AddReportAsync(
            factory,
            wishlist.Id,
            WishlistReportReason.Other,
            ct);
        var wish = new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "Gift",
            null,
            null,
            null,
            1);
        var firstId = Guid.CreateVersion7();
        var firstBytes = CreateWebP(SKColors.Red);
        wish.ReplaceImage(
            firstId,
            SHA256.HashData(firstBytes));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
        db.Wishes.Add(wish);
        await db.SaveChangesAsync(ct);
        await store.WritePendingAsync(
            firstId,
            firstBytes,
            ct);
        using var client = ReportedWishlistTestData.CreateClient(
            factory,
            adminId);
        var route = $"/api/v1/admin/reported-wishlists/{wishlist.Id}/wishes/{wish.Id}/image";
        var nextId = Guid.CreateVersion7();
        var nextBytes = CreateWebP(SKColors.Blue);

        // Act
        using var initial = await client.GetAsync(
            route,
            ct);
        wish.ReplaceImage(
            nextId,
            SHA256.HashData(nextBytes));
        await store.WritePendingAsync(
            nextId,
            nextBytes,
            ct);
        await db.SaveChangesAsync(ct);
        using var replaced = await client.GetAsync(
            route,
            ct);
        await store.DeleteAsync(
            nextId,
            ct);
        using var unavailable = await client.GetAsync(
            route,
            ct);
        wish.RemoveImage();
        await db.SaveChangesAsync(ct);
        using var removed = await client.GetAsync(
            route,
            ct);
        db.Wishes.Remove(wish);
        await db.SaveChangesAsync(ct);
        using var deleted = await client.GetAsync(
            route,
            ct);

        // Assert
        Assert.Equal(
            firstBytes,
            await initial.Content.ReadAsByteArrayAsync(ct));
        Assert.Equal(
            nextBytes,
            await replaced.Content.ReadAsByteArrayAsync(ct));
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            unavailable.StatusCode);
        Assert.DoesNotContain(
            _storagePath,
            await unavailable.Content.ReadAsStringAsync(ct),
            StringComparison.Ordinal);
        Assert.Equal(
            HttpStatusCode.NotFound,
            removed.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            deleted.StatusCode);
    }

    private static byte[] CreateWebP(SKColor color)
    {
        using var bitmap = new SKBitmap(
            2,
            2);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(
            SKEncodedImageFormat.Webp,
            82);

        return data.ToArray();
    }
}
