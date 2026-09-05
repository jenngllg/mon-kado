using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using SkiaSharp;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishImageIntegrationTests(PostgreSqlContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset _referenceTime = new(
        2026,
        9,
        5,
        12,
        0,
        0,
        TimeSpan.Zero);

    private readonly string _storagePath = Path.Combine(
        Path.GetTempPath(),
        "mon-kado-gift-image-integration-tests",
        Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_storagePath))
            Directory.Delete(
                _storagePath,
                recursive: true);
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task UpsertImageAsync_WhenAddingRepeatingAndReplacing_PersistsHashAndCleansThroughOutbox()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Images");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creation = await CreateWishAsync(
            client,
            wishlist.Id);
        var wishId = await ReadWishIdAsync(creation);
        var firstSource = CreatePng(SKColors.Purple);
        using var firstRequest = CreateUpsertRequest(
            wishlist.Id,
            wishId,
            firstSource,
            creation.Headers.ETag?.Tag);

        // Act
        using var firstResponse = await client.SendAsync(
            firstRequest,
            TestContext.Current.CancellationToken);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var firstUrl = firstBody.GetProperty("imageUrl").GetString()
            ?? throw new InvalidOperationException("The first signed image URL is missing.");
        var firstState = await GetPersistedStateAsync(
            factory,
            wishId);
        using var repeatedRequest = CreateUpsertRequest(
            wishlist.Id,
            wishId,
            firstSource,
            firstResponse.Headers.ETag?.Tag);
        using var repeatedResponse = await client.SendAsync(
            repeatedRequest,
            TestContext.Current.CancellationToken);
        var repeatedState = await GetPersistedStateAsync(
            factory,
            wishId);
        using var replacementRequest = CreateUpsertRequest(
            wishlist.Id,
            wishId,
            CreatePng(SKColors.Green),
            repeatedResponse.Headers.ETag?.Tag);
        using var replacementResponse = await client.SendAsync(
            replacementRequest,
            TestContext.Current.CancellationToken);
        var replacementBody = await replacementResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var replacementUrl = replacementBody.GetProperty("imageUrl").GetString()
            ?? throw new InvalidOperationException("The replacement signed image URL is missing.");
        var replacementState = await GetPersistedStateAsync(
            factory,
            wishId);
        using var obsoleteImageResponse = await client.GetAsync(
            firstUrl,
            TestContext.Current.CancellationToken);
        using var currentImageResponse = await client.GetAsync(
            replacementUrl,
            TestContext.Current.CancellationToken);
        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlist.Id}/wishes/{wishId}");
        await AssertAndProcessOutboxAsync(
            factory,
            firstState.ImageId);
        deleteRequest.Headers.TryAddWithoutValidation(
            "If-Match",
            replacementResponse.Headers.ETag?.Tag);
        using var deleteResponse = await client.SendAsync(
            deleteRequest,
            TestContext.Current.CancellationToken);
        using var deletedImageResponse = await client.GetAsync(
            replacementUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            repeatedResponse.StatusCode);
        Assert.Equal(
            firstResponse.Headers.ETag?.Tag,
            repeatedResponse.Headers.ETag?.Tag);
        Assert.Equal(
            firstState.ImageId,
            repeatedState.ImageId);
        Assert.Equal(
            firstState.ContentHash,
            repeatedState.ContentHash);
        Assert.Equal(
            HttpStatusCode.OK,
            replacementResponse.StatusCode);
        Assert.NotEqual(
            repeatedResponse.Headers.ETag?.Tag,
            replacementResponse.Headers.ETag?.Tag);
        Assert.NotEqual(
            firstState.ImageId,
            replacementState.ImageId);
        Assert.NotEqual(
            firstState.ContentHash,
            replacementState.ContentHash);
        Assert.Equal(
            HttpStatusCode.NotFound,
            obsoleteImageResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            currentImageResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            deleteResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            deletedImageResponse.StatusCode);
        Assert.Empty(Directory.EnumerateFiles(
            _storagePath,
            "*.pending",
            SearchOption.AllDirectories));

        await AssertAndProcessOutboxAsync(
            factory,
            replacementState.ImageId);
    }

    [Fact]
    public async Task UpsertImageAsync_WhenTwoReplacementsRace_AllowsOneAndLeavesLoserForPendingReconciliation()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Concurrent images");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creation = await CreateWishAsync(
            client,
            wishlist.Id);
        var wishId = await ReadWishIdAsync(creation);
        using var firstRequest = CreateUpsertRequest(
            wishlist.Id,
            wishId,
            CreatePng(SKColors.Blue),
            creation.Headers.ETag?.Tag);
        using var secondRequest = CreateUpsertRequest(
            wishlist.Id,
            wishId,
            CreatePng(SKColors.Orange),
            creation.Headers.ETag?.Tag);

        // Act
        var firstTask = client.SendAsync(
            firstRequest,
            TestContext.Current.CancellationToken);
        var secondTask = client.SendAsync(
            secondRequest,
            TestContext.Current.CancellationToken);
        var responses = await Task.WhenAll(
            firstTask,
            secondTask);

        // Assert
        try
        {
            Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.PreconditionFailed);
            var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
            var winnerBody = await winner.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken);
            var winnerUrl = winnerBody.GetProperty("imageUrl").GetString()
                ?? throw new InvalidOperationException("The winning signed image URL is missing.");
            using var currentImageResponse = await client.GetAsync(
                winnerUrl,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.OK,
                currentImageResponse.StatusCode);
            var pendingFile = Assert.Single(Directory.EnumerateFiles(
                _storagePath,
                "*.pending",
                SearchOption.AllDirectories));
            var pendingId = Guid.ParseExact(
                Path.GetFileNameWithoutExtension(pendingFile),
                "N");
            await using var scope = factory.Services.CreateAsyncScope();
            var cleanupService = scope.ServiceProvider.GetRequiredService<IGiftImageCleanupService>();
            Assert.False(await cleanupService.IsReferencedAsync(
                pendingId,
                TestContext.Current.CancellationToken));
            File.SetLastWriteTimeUtc(
                pendingFile,
                _referenceTime.UtcDateTime.AddHours(-2));
            var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
            var pending = await store.GetPendingAsync(
                _referenceTime.UtcDateTime.AddHours(-1),
                10,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                pendingId,
                Assert.Single(pending).ImageId);
            await store.DeleteAsync(
                pendingId,
                TestContext.Current.CancellationToken);
            Assert.False(File.Exists(pendingFile));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task IsSharedImageCurrentAsync_WhenShareLinkIsRevoked_InvalidatesImageAccess()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Shared image");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creation = await CreateWishAsync(
            client,
            wishlist.Id);
        var wishId = await ReadWishIdAsync(creation);
        using var upsertRequest = CreateUpsertRequest(
            wishlist.Id,
            wishId,
            CreatePng(SKColors.Red),
            creation.Headers.ETag?.Tag);
        using var upsertResponse = await client.SendAsync(
            upsertRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            upsertResponse.StatusCode);
        var state = await GetPersistedStateAsync(
            factory,
            wishId);
        var shareLink = new WishlistShareLink(
            Guid.CreateVersion7(),
            wishlist.Id,
            Enumerable.Repeat(
                (byte)7,
                32).ToArray(),
            "protected-secret");
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            context.WishlistShareLinks.Add(shareLink);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        bool isCurrentBeforeRevocation;

        await using (var accessScope = factory.Services.CreateAsyncScope())
        {
            var accessService = accessScope.ServiceProvider.GetRequiredService<IWishImageAccessService>();
            isCurrentBeforeRevocation = await accessService.IsSharedImageCurrentAsync(
                shareLink.Id,
                wishlist.Id,
                wishId,
                state.ImageId,
                TestContext.Current.CancellationToken);
        }

        // Act
        await using (var revokeScope = factory.Services.CreateAsyncScope())
        {
            var context = revokeScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var persistedShareLink = await context.WishlistShareLinks.SingleAsync(
                TestContext.Current.CancellationToken);
            context.WishlistShareLinks.Remove(persistedShareLink);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        bool isCurrentAfterRevocation;

        await using (var accessScope = factory.Services.CreateAsyncScope())
        {
            var accessService = accessScope.ServiceProvider.GetRequiredService<IWishImageAccessService>();
            isCurrentAfterRevocation = await accessService.IsSharedImageCurrentAsync(
                shareLink.Id,
                wishlist.Id,
                wishId,
                state.ImageId,
                TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.True(isCurrentBeforeRevocation);
        Assert.False(isCurrentAfterRevocation);
    }

    [Theory]
    [InlineData("image", false, false)]
    [InlineData("wish", false, false)]
    [InlineData("wishlist", false, false)]
    [InlineData("image", true, false)]
    [InlineData("wish", true, false)]
    [InlineData("wishlist", true, false)]
    [InlineData("image", false, true)]
    [InlineData("wish", false, true)]
    [InlineData("wishlist", false, true)]
    public async Task DeleteAsync_WhenImageOrParentIsRemoved_InvalidatesUrlAndQueuesFile(
            string target,
            bool losesCommitAcknowledgement,
            bool failsBeforeCommit)
    {
        // Arrange
        var interceptor = new GiftImageCommitInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Image deletion");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creation = await CreateWishAsync(
            client,
            wishlist.Id);
        var wishId = await ReadWishIdAsync(creation);
        using var upload = CreateUpsertRequest(
            wishlist.Id,
            wishId,
            CreatePng(SKColors.Blue),
            creation.Headers.ETag?.Tag);
        using var uploaded = await client.SendAsync(
            upload,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            uploaded.StatusCode);
        var body = await uploaded.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var imageUrl = body.GetProperty("imageUrl").GetString()
            ?? throw new InvalidOperationException("Missing image URL.");
        var state = await GetPersistedStateAsync(
            factory,
            wishId);
        var shareLinkId = Guid.CreateVersion7();
        var expectedImageIds = new List<Guid> { state.ImageId };

        if (target == "wishlist")
        {
            using var secondCreation = await CreateWishAsync(
                client,
                wishlist.Id);
            var secondWishId = await ReadWishIdAsync(secondCreation);
            using var secondUpload = CreateUpsertRequest(
                wishlist.Id,
                secondWishId,
                CreatePng(SKColors.Red),
                secondCreation.Headers.ETag?.Tag);
            using var secondUploaded = await client.SendAsync(
                secondUpload,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.OK,
                secondUploaded.StatusCode);
            var secondState = await GetPersistedStateAsync(
                factory,
                secondWishId);
            expectedImageIds.Add(secondState.ImageId);
            using var withoutImage = await CreateWishAsync(
                client,
                wishlist.Id);
            Assert.Equal(
                HttpStatusCode.Created,
                withoutImage.StatusCode);
        }

        string sharedImageUrl;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            context.WishlistShareLinks.Add(new WishlistShareLink(
                shareLinkId,
                wishlist.Id,
                new byte[32],
                "protected-secret"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            var urls = new WishImageUrlService(
                scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
                new HttpContextAccessor { HttpContext = httpContext },
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            sharedImageUrl = urls.CreateSharedUrl(
                shareLinkId,
                wishlist.Id,
                wishId,
                state.ImageId);
        }
        using var sharedBefore = await client.GetAsync(
            sharedImageUrl,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            sharedBefore.StatusCode);
        var route = $"/api/v1/wishlists/{wishlist.Id}/wishes/{wishId}";
        var entityTag = uploaded.Headers.ETag?.Tag;

        if (target == "image")
            route += "/image";

        if (target == "wishlist")
        {
            route = $"/api/v1/wishlists/{wishlist.Id}";
            using var listResponse = await client.GetAsync(
                route,
                TestContext.Current.CancellationToken);
            entityTag = listResponse.Headers.ETag?.Tag;
        }
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            route);
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        // Act
        if (losesCommitAcknowledgement)
            interceptor.Arm();

        if (failsBeforeCommit)
            interceptor.ArmBeforeCommit();

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        using var oldImage = await client.GetAsync(
            imageUrl,
            TestContext.Current.CancellationToken);
        using var sharedAfter = await client.GetAsync(
            sharedImageUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            failsBeforeCommit ? HttpStatusCode.OK : HttpStatusCode.NotFound,
            sharedAfter.StatusCode);
        if (failsBeforeCommit)
        {
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                response.StatusCode);
            Assert.Equal(
                HttpStatusCode.OK,
                oldImage.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            Assert.Empty(await context.GiftImageDeletionOutboxMessages
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken));
            var unchanged = await GetPersistedStateAsync(
                factory,
                wishId);
            Assert.Equal(
                state.ImageId,
                unchanged.ImageId);

            return;
        }

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            HttpStatusCode.NotFound,
            oldImage.StatusCode);

        if (target == "image")
        {
            Assert.NotEqual(
                entityTag,
                response.Headers.ETag?.Tag);
            using var gift = await client.GetAsync(
                $"/api/v1/wishlists/{wishlist.Id}/wishes/{wishId}",
                TestContext.Current.CancellationToken);
            var giftBody = await gift.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken);
            Assert.Equal(
                JsonValueKind.Null,
                giftBody.GetProperty("imageUrl").ValueKind);
            using var staleRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                route);
            staleRequest.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);
            using var stale = await client.SendAsync(
                staleRequest,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.PreconditionFailed,
                stale.StatusCode);
            using var repeatRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                route);
            repeatRequest.Headers.TryAddWithoutValidation(
                "If-Match",
                response.Headers.ETag?.Tag);
            using var repeat = await client.SendAsync(
                repeatRequest,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.NotFound,
                repeat.StatusCode);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
            await using var file = await store.OpenReadAsync(
                state.ImageId,
                TestContext.Current.CancellationToken);
            Assert.NotNull(file);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var queued = await context.GiftImageDeletionOutboxMessages
                .AsNoTracking()
                .Select(message => message.ImageId)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            Assert.Equal(
                expectedImageIds.Order(),
                queued.Order());
            var cleanup = scope.ServiceProvider.GetRequiredService<IGiftImageCleanupService>();
            var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
            foreach (var imageId in expectedImageIds)
            {
                var deletion = await cleanup.ClaimNextAsync(
                    _referenceTime.UtcDateTime,
                    TimeSpan.FromMinutes(5),
                    TestContext.Current.CancellationToken);
                Assert.NotNull(deletion);
                await store.DeleteAsync(
                    deletion.ImageId,
                    TestContext.Current.CancellationToken);
                await store.DeleteAsync(
                    deletion.ImageId,
                    TestContext.Current.CancellationToken);
                await cleanup.CompleteAsync(
                    deletion.Id,
                    TestContext.Current.CancellationToken);
                Assert.Null(await store.OpenReadAsync(
                    deletion.ImageId,
                    TestContext.Current.CancellationToken));
            }
            Assert.Empty(await context.GiftImageDeletionOutboxMessages
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("image")]
    [InlineData("wish")]
    [InlineData("wishlist")]
    [InlineData("wishlist-add")]
    public async Task DeleteAsync_WhenReplacementRaces_DoesNotLoseImageCleanup(
            string target)
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Concurrent deletion");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creation = await CreateWishAsync(
            client,
            wishlist.Id);
        var wishId = await ReadWishIdAsync(creation);
        using var upload = CreateUpsertRequest(
            wishlist.Id,
            wishId,
            CreatePng(SKColors.Blue),
            creation.Headers.ETag?.Tag);
        using var uploaded = await client.SendAsync(
            upload,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            uploaded.StatusCode);
        var route = $"/api/v1/wishlists/{wishlist.Id}/wishes/{wishId}";
        var entityTag = uploaded.Headers.ETag?.Tag;

        if (target == "image")
            route += "/image";

        if (target is "wishlist" or "wishlist-add")
        {
            route = $"/api/v1/wishlists/{wishlist.Id}";
            using var list = await client.GetAsync(
                route,
                TestContext.Current.CancellationToken);
            entityTag = list.Headers.ETag?.Tag;
        }
        using var deletion = new HttpRequestMessage(
            HttpMethod.Delete,
            route);
        deletion.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);
        using var replacement = CreateUpsertRequest(
            wishlist.Id,
            wishId,
            CreatePng(SKColors.Red),
            uploaded.Headers.ETag?.Tag);

        // Act
        var deleteTask = client.SendAsync(
            deletion,
            TestContext.Current.CancellationToken);
        var replaceTask = target == "wishlist-add"
            ? CreateWishAsync(
                client,
                wishlist.Id)
            : client.SendAsync(
                replacement,
                TestContext.Current.CancellationToken);
        using var deleted = await deleteTask;
        using var replaced = await replaceTask;

        // Assert
        Assert.True(
            deleted.IsSuccessStatusCode || replaced.IsSuccessStatusCode,
            $"Both concurrent operations failed: DELETE {(int)deleted.StatusCode}, competing operation {(int)replaced.StatusCode}.");
        Assert.Contains(
            deleted.StatusCode,
            (HttpStatusCode[])[
                HttpStatusCode.NoContent,
                HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.PreconditionFailed
            ]);
        Assert.Contains(
            replaced.StatusCode,
            (HttpStatusCode[])[
                HttpStatusCode.OK,
                HttpStatusCode.Created,
                HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.PreconditionFailed,
                HttpStatusCode.NotFound
            ]);

        if (target == "image")
            Assert.NotEqual(
                deleted.StatusCode == HttpStatusCode.NoContent,
                replaced.StatusCode == HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var references = await context.Wishes
            .AsNoTracking()
            .Where(wish => wish.ImageId.HasValue)
            .Select(wish => wish.ImageId.GetValueOrDefault())
            .ToArrayAsync(TestContext.Current.CancellationToken);

        if (target is "wishlist" or "wishlist-add" && deleted.StatusCode == HttpStatusCode.NoContent)
        {
            Assert.False(await context.Wishes
                .AsNoTracking()
                .AnyAsync(
                    wish => wish.WishlistId == wishlist.Id,
                    TestContext.Current.CancellationToken));
        }

        var queued = await context.GiftImageDeletionOutboxMessages
            .AsNoTracking()
            .Select(message => message.ImageId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
        var pending = await store.GetPendingAsync(
            DateTime.MaxValue,
            100,
            TestContext.Current.CancellationToken);
        var accountedFor = references
            .Concat(queued)
            .Concat(pending.Select(image => image.ImageId))
            .ToHashSet();
        foreach (var file in Directory.EnumerateFiles(
            _storagePath,
            "*.webp",
            SearchOption.AllDirectories))
        {
            Assert.Contains(
                Guid.ParseExact(
                    Path.GetFileNameWithoutExtension(file),
                    "N"),
                accountedFor);
        }
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(
        AmbiguousCommitInterceptor? interceptor = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            new FixedTimeProvider(_referenceTime),
            configureServices: services =>
            {
                if (interceptor is not null)
                    services.ConfigureDbContext<MonKadoDbContext>((_, options) => options.AddInterceptors(interceptor));
            },
            giftImageStoragePath: _storagePath);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task<MonKadoUser> CreateMemberAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_referenceTime),
            Email = "image-owner@example.fr",
            UserName = "image-owner@example.fr",
            DisplayName = "Image Owner",
            EmailConfirmed = true
        };
        var creationResult = await userManager.CreateAsync(member);
        Assert.True(
            creationResult.Succeeded,
            string.Join(
                ", ",
                creationResult.Errors.Select(error => error.Description)));
        var roleResult = await userManager.AddToRoleAsync(
            member,
            RoleNames.Member);
        Assert.True(
            roleResult.Succeeded,
            string.Join(
                ", ",
                roleResult.Errors.Select(error => error.Description)));

        return member;
    }

    private static async Task<Wishlist> SeedWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid ownerId,
        string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var wishlist = new Wishlist(
            Guid.CreateVersion7(),
            ownerId,
            name,
            name.ToUpperInvariant(),
            WishlistOccasion.Birthday,
            null,
            null);
        context.Wishlists.Add(wishlist);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return wishlist;
    }

    private static HttpClient CreateAuthorizedClient(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var jwtOptions = factory.Services.GetRequiredService<IOptions<JwtOptions>>();
        var accessTokenService = new JwtAccessTokenService(
            jwtOptions,
            TimeProvider.System);
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
    }

    private static Task<HttpResponseMessage> CreateWishAsync(
        HttpClient client,
        Guid wishlistId)
    {
        return client.PostAsJsonAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes",
            new
            {
                name = "Gift"
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> ReadWishIdAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        return body.GetProperty("id").GetGuid();
    }

    private static HttpRequestMessage CreateUpsertRequest(
        Guid wishlistId,
        Guid wishId,
        byte[] content,
        string? entityTag)
    {
        var multipart = new MultipartFormDataContent();
        var image = new ByteArrayContent(content);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(
            image,
            "image",
            "source.png");
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}/image")
        {
            Content = multipart
        };

        if (entityTag is not null)
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);

        return request;
    }

    private static byte[] CreatePng(SKColor color)
    {
        using var bitmap = new SKBitmap(
            4,
            4,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var content = image.Encode(
            SKEncodedImageFormat.Png,
            100);

        return content.ToArray();
    }

    private static async Task<(
        Guid ImageId,
        byte[] ContentHash)> GetPersistedStateAsync(
        PostgreSqlApiFactory factory,
        Guid wishId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var wish = await context.Wishes
            .AsNoTracking()
            .SingleAsync(
                wish => wish.Id == wishId,
                TestContext.Current.CancellationToken);
        Assert.NotNull(wish.ImageId);
        Assert.NotNull(wish.ImageContentHash);

        return (
            wish.ImageId.Value,
            wish.ImageContentHash);
    }

    private static async Task AssertAndProcessOutboxAsync(
        PostgreSqlApiFactory factory,
        Guid obsoleteImageId)
    {
        await using var firstScope = factory.Services.CreateAsyncScope();
        var firstCleanup = firstScope.ServiceProvider.GetRequiredService<IGiftImageCleanupService>();
        await firstCleanup.CompleteAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);
        await firstCleanup.ScheduleRetryAsync(
            Guid.CreateVersion7(),
            _referenceTime.UtcDateTime,
            TestContext.Current.CancellationToken);
        var deletion = await firstCleanup.ClaimNextAsync(
            _referenceTime.UtcDateTime,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);
        Assert.NotNull(deletion);
        Assert.Equal(
            obsoleteImageId,
            deletion.ImageId);
        Assert.Equal(
            1,
            deletion.AttemptCount);
        await using var secondScope = factory.Services.CreateAsyncScope();
        var secondCleanup = secondScope.ServiceProvider.GetRequiredService<IGiftImageCleanupService>();
        Assert.Null(await secondCleanup.ClaimNextAsync(
            _referenceTime.UtcDateTime,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken));
        var nextAttemptAt = _referenceTime.UtcDateTime.AddHours(1);
        await firstCleanup.ScheduleRetryAsync(
            deletion.Id,
            nextAttemptAt,
            TestContext.Current.CancellationToken);
        Assert.Null(await secondCleanup.ClaimNextAsync(
            _referenceTime.UtcDateTime,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken));
        await using var retryScope = factory.Services.CreateAsyncScope();
        var retryCleanup = retryScope.ServiceProvider.GetRequiredService<IGiftImageCleanupService>();
        var retryDeletion = await retryCleanup.ClaimNextAsync(
            nextAttemptAt,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);
        Assert.NotNull(retryDeletion);
        Assert.Equal(
            2,
            retryDeletion.AttemptCount);
        var store = firstScope.ServiceProvider.GetRequiredService<IGiftImageStore>();
        await store.DeleteAsync(
            obsoleteImageId,
            TestContext.Current.CancellationToken);
        await retryCleanup.CompleteAsync(
            retryDeletion.Id,
            TestContext.Current.CancellationToken);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.GiftImageDeletionOutboxMessages
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Null(await store.OpenReadAsync(
            obsoleteImageId,
            TestContext.Current.CancellationToken));
    }
}
