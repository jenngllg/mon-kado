using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class OwnedGiftReservationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpsertAsync_WhenOwnerReservesWithoutSharing_UsesNormalVersionedReservationAndPrivateHistory(bool surpriseMode)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            surpriseMode,
            3,
            cancellationToken);
        await AddImageAsync(
            factory,
            seed.WishId,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);

        // Act
        using var created = await MutateAsync(
            client,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Put,
            2,
            null,
            cancellationToken);
        using var read = await client.GetAsync(
            Path(
                seed.WishlistId,
                seed.WishId),
            cancellationToken);
        using var history = await client.GetAsync(
            "/api/v1/members/current/reservations",
            cancellationToken);
        using var detail = await client.GetAsync(
            $"/api/v1/wishlists/{seed.WishlistId}/wishes/{seed.WishId}",
            cancellationToken);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var historyBody = await history.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            created.StatusCode);
        Assert.EndsWith(
            Path(
                seed.WishlistId,
                seed.WishId),
            created.Headers.Location?.ToString());
        Assert.NotNull(created.Headers.ETag);
        Assert.Equal(
            HttpStatusCode.OK,
            read.StatusCode);
        Assert.Equal(
            2,
            createdBody.GetProperty("quantity").GetInt32());
        var item = Assert.Single(historyBody.GetProperty("items").EnumerateArray());
        Assert.Equal(
            $"/lists/{seed.WishlistId}/wishes/{seed.WishId}",
            item.GetProperty("ownedWishPath").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            item.GetProperty("shareUrl").ValueKind);
        Assert.Contains(
            $"/api/v1/wishlists/{seed.WishlistId}/wishes/{seed.WishId}/image?token=",
            item.GetProperty("imageUrl").GetString());
        Assert.Equal(
            surpriseMode ? JsonValueKind.Null : JsonValueKind.Number,
            detailBody.GetProperty("reservedQuantity").ValueKind);
        Assert.Equal(
            surpriseMode ? JsonValueKind.Null : JsonValueKind.Number,
            detailBody.GetProperty("availableQuantity").ValueKind);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            0,
            await database.WishlistShareLinks.CountAsync(cancellationToken));
        Assert.Equal(
            1,
            await database.WishlistParticipants.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task UpsertAsync_WhenVersionChanges_RejectsDuplicatesAndStaleVersionsAndCancelsOnlyOwnReservation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            false,
            3,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);
        using var created = await MutateAsync(
            client,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Put,
            1,
            null,
            cancellationToken);
        var originalVersion = created.Headers.ETag?.ToString();

        // Act
        using var duplicate = await MutateAsync(
            client,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Put,
            2,
            null,
            cancellationToken);
        using var updated = await MutateAsync(
            client,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Put,
            2,
            originalVersion,
            cancellationToken);
        using var stale = await MutateAsync(
            client,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Delete,
            null,
            originalVersion,
            cancellationToken);
        using var cancelled = await MutateAsync(
            client,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Delete,
            null,
            updated.Headers.ETag?.ToString(),
            cancellationToken);
        using var read = await client.GetAsync(
            Path(
                seed.WishlistId,
                seed.WishId),
            cancellationToken);
        using var history = await client.GetAsync(
            "/api/v1/members/current/reservations?status=cancelled",
            cancellationToken);
        var body = await history.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            (HttpStatusCode)428,
            duplicate.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            updated.StatusCode);
        Assert.NotEqual(
            originalVersion,
            updated.Headers.ETag?.ToString());
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            stale.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            cancelled.StatusCode);
        Assert.Empty(await cancelled.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal(
            HttpStatusCode.NotFound,
            read.StatusCode);
        Assert.Equal(
            "cancelled",
            Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(null)]
    public async Task UpsertAsync_WhenQuantityIsInvalid_ReturnsAggregatedValidationWithoutCreatingParticipation(int? quantity)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            true,
            3,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);

        // Act
        using var result = await MutateAsync(
            client,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Put,
            quantity,
            null,
            cancellationToken);
        var body = await result.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            result.StatusCode);
        Assert.Equal(
            "quantity",
            Assert.Single(body.GetProperty("validationErrors").EnumerateArray()).GetProperty("propertyName").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(
            0,
            await scope.ServiceProvider.GetRequiredService<MonKadoDbContext>().WishlistParticipants.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task GetAsync_WhenReservationIsAbsent_DoesNotCreateParticipationOrHistory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            true,
            1,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);

        // Act
        using var read = await client.GetAsync(
            Path(
                seed.WishlistId,
                seed.WishId),
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            read.StatusCode);
        using var cancelled = await MutateAsync(
            client,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Delete,
            null,
            "\"00000001\"",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.NotFound,
            cancelled.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            0,
            await database.WishlistParticipants.CountAsync(cancellationToken));
        Assert.Equal(
            0,
            await database.GiftReservationHistories.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task UpsertAsync_WhenForeignOrUnauthenticatedOrWithoutCsrf_DoesNotGrantOwnerAccess()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            false,
            1,
            cancellationToken);
        using var foreign = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OtherId,
            cancellationToken);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);
        using var anonymous = factory.CreateClient();

        // Act
        using var unauthorized = await anonymous.GetAsync(
            Path(
                seed.WishlistId,
                seed.WishId),
            cancellationToken);
        using var forbidden = await MutateAsync(
            foreign,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Put,
            1,
            null,
            cancellationToken);
        using var csrf = await owner.PutAsJsonAsync(
            Path(
                seed.WishlistId,
                seed.WishId),
            new
            {
                quantity = 1
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            unauthorized.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            forbidden.StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            csrf.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpsertAsync_WhenOtherParticipantConsumesCapacity_RejectsOverbookingWithoutDisclosingHiddenQuantity(bool surpriseMode)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            surpriseMode,
            3,
            cancellationToken);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var participant = WishlistParticipant.CreateMember(
                Guid.CreateVersion7(),
                seed.WishlistId,
                seed.OtherId);
            database.WishlistParticipants.Add(participant);
            database.GiftReservations.Add(new GiftReservation(
                Guid.CreateVersion7(),
                seed.WishlistId,
                seed.WishId,
                participant.Id,
                2));
            await database.SaveChangesAsync(cancellationToken);
        }
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);

        // Act
        using var rejected = await MutateAsync(
            owner,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Put,
            2,
            null,
            cancellationToken);
        using var accepted = await MutateAsync(
            owner,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Put,
            1,
            null,
            cancellationToken);
        var error = await rejected.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            rejected.StatusCode);
        Assert.Equal(
            "GIFT_RESERVATION_QUANTITY_UNAVAILABLE",
            error.GetProperty("errorCode").GetString());
        Assert.Equal(
            5,
            error.EnumerateObject().Count());
        Assert.Equal(
            HttpStatusCode.Created,
            accepted.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpsertAsync_WhenParentIsArchivedOrSuspended_RejectsReservation(bool suspended)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            true,
            1,
            cancellationToken);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var list = await database.Wishlists.SingleAsync(
                item => item.Id == seed.WishlistId,
                cancellationToken);

            if (suspended)
                list.Moderate(
                    true,
                    "Test",
                    DateTime.UnixEpoch);

            if (!suspended)
                list.SetArchived(true);
            await database.SaveChangesAsync(cancellationToken);
        }
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);

        // Act
        using var result = await MutateAsync(
            owner,
            seed.WishlistId,
            seed.WishId,
            HttpMethod.Put,
            1,
            null,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            result.StatusCode);
    }

    [Fact]
    public async Task UpsertAsync_WhenOwnerAndSharedParticipantRaceForLastUnit_OnlyOneReservationSucceeds()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var seed = await SeedAsync(
            factory,
            true,
            1,
            cancellationToken);
        Guid linkId;
        string secret;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var token = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>().Create();
            linkId = Guid.CreateVersion7();
            secret = token.Secret;
            database.WishlistShareLinks.Add(new WishlistShareLink(
                linkId,
                seed.WishlistId,
                token.SecretHash,
                token.ProtectedSecret));
            database.WishlistParticipants.Add(WishlistParticipant.CreateMember(
                Guid.CreateVersion7(),
                seed.WishlistId,
                seed.OtherId));
            await database.SaveChangesAsync(cancellationToken);
        }
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OwnerId,
            cancellationToken);
        using var participant = await AuthenticationTestData.CreateClientAsync(
            factory,
            seed.OtherId,
            cancellationToken);
        using var csrfResponse = await participant.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var sharedRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/shared-wishlists/{linkId}/wishes/{seed.WishId}/reservations/current")
        {
            Content = JsonContent.Create(new { quantity = 1 })
        };
        sharedRequest.Headers.Add(
            "X-CSRF-TOKEN",
            csrf.GetProperty("token").GetString());
        sharedRequest.Headers.Add(
            "X-MonKado-Share-Token",
            secret);

        // Act
        var results = await Task.WhenAll(
            MutateAsync(
                owner,
                seed.WishlistId,
                seed.WishId,
                HttpMethod.Put,
                1,
                null,
                cancellationToken),
            participant.SendAsync(
                sharedRequest,
                cancellationToken));

        // Assert
        using var ownedResult = results[0];
        using var sharedResult = results[1];
        Assert.Single(
            results,
            result => result.StatusCode == HttpStatusCode.Created);
        Assert.Single(
            results,
            result => result.StatusCode == HttpStatusCode.Conflict);
        await using var verification = factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1,
            await context.GiftReservations.SumAsync(
                reservation => reservation.Quantity,
                cancellationToken));
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(CancellationToken cancellationToken)
    {
        await fixture.ResetDatabaseAsync(cancellationToken);

        return new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
    }

    private static async Task AddImageAsync(
        PostgreSqlApiFactory factory,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var wish = await database.Wishes.SingleAsync(
            item => item.Id == wishId,
            cancellationToken);
        using var bitmap = new SKBitmap(
            4,
            4);
        bitmap.Erase(SKColors.Green);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(
            SKEncodedImageFormat.Webp,
            82);
        var content = encoded.ToArray();
        var imageId = Guid.CreateVersion7();
        var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
        await store.WritePendingAsync(
            imageId,
            content,
            cancellationToken);
        await store.MarkCommittedAsync(
            imageId,
            cancellationToken);
        wish.ReplaceImage(
            imageId,
            SHA256.HashData(content));
        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task<(Guid OwnerId, Guid OtherId, Guid WishlistId, Guid WishId)> SeedAsync(
        PostgreSqlApiFactory factory,
        bool surpriseMode,
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var ownerId = Guid.CreateVersion7();
        var otherId = Guid.CreateVersion7();
        var memberIds = new[]
        {
            ownerId,
            otherId
        };
        database.Users.AddRange(memberIds.Select(id => new MonKadoUser
        {
            Id = id,
            DisplayName = "Test member",
            EmailConfirmed = true,
            UserName = $"{id}@example.test",
            NormalizedUserName = $"{id}@EXAMPLE.TEST".ToUpperInvariant(),
            Email = $"{id}@example.test",
            NormalizedEmail = $"{id}@EXAMPLE.TEST".ToUpperInvariant(),
            SecurityStamp = Guid.CreateVersion7().ToString()
        }));
        var wishlist = new Wishlist(
            Guid.CreateVersion7(),
            ownerId,
            "Owned list",
            "OWNED LIST",
            WishlistOccasion.Other,
            null,
            null,
            surpriseMode);
        var wish = new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "Owned wish",
            null,
            null,
            null,
            1,
            quantity);
        database.Wishlists.Add(wishlist);
        database.Wishes.Add(wish);
        await database.SaveChangesAsync(cancellationToken);

        return (ownerId, otherId, wishlist.Id, wish.Id);
    }

    private static string Path(
        Guid wishlistId,
        Guid wishId)
    {

        return $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}/reservations/current";
    }

    private static async Task<HttpResponseMessage> MutateAsync(
        HttpClient client,
        Guid wishlistId,
        Guid wishId,
        HttpMethod method,
        int? quantity,
        string? etag,
        CancellationToken cancellationToken)
    {
        using var csrfResponse = await client.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var request = new HttpRequestMessage(
            method,
            Path(wishlistId, wishId));
        request.Headers.Add(
            "X-CSRF-TOKEN",
            csrf.GetProperty("token").GetString());

        if (method == HttpMethod.Put)
            request.Content = JsonContent.Create(new
            {
                quantity
            });

        if (etag is not null)
            request.Headers.Add(
                "If-Match",
                etag);

        return await client.SendAsync(
            request,
            cancellationToken);
    }
}
