using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using Npgsql;

using SkiaSharp;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishCopyIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("missing-secret", HttpStatusCode.NotFound)]
    [InlineData("empty-body", HttpStatusCode.BadRequest)]
    [InlineData("empty-identifiers", HttpStatusCode.BadRequest)]
    [InlineData("missing-csrf", HttpStatusCode.BadRequest)]
    public async Task CopyAsync_WhenRequestIsInvalid_DoesNotCreateAWish(
        string scenario,
        HttpStatusCode expectedStatus)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var seeded = await SeedAsync(
            factory,
            false);
        using var client = scenario == "anonymous"
            ? factory.CreateClient()
            : await AuthenticationTestData.CreateClientAsync(
                factory,
                seeded.Destination.OwnerId,
                cancellationToken);
        Guid? sourceShareLinkId = seeded.Link.Id;
        Guid? sourceWishId = seeded.Source.Id;

        if (scenario == "empty-body")
        {
            sourceShareLinkId = null;
            sourceWishId = null;
        }

        if (scenario == "empty-identifiers")
        {
            sourceShareLinkId = Guid.Empty;
            sourceWishId = Guid.Empty;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/wishlists/{seeded.Destination.Id}/wishes/copies")
        {
            Content = JsonContent.Create(new
            {
                sourceShareLinkId,
                sourceWishId
            })
        };

        if (scenario != "missing-secret")
            request.Headers.Add(
                "X-MonKado-Share-Token",
                seeded.Secret);

        if (scenario is not ("anonymous" or "missing-csrf"))
            request.Headers.Add(
                "X-CSRF-TOKEN",
                await GetCsrfTokenAsync(
                    client,
                    cancellationToken));

        // Act
        using var response = await client.SendAsync(
            request,
            cancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await database.Wishes.AnyAsync(
            wish => wish.WishlistId == seeded.Destination.Id,
            cancellationToken));
    }

    [Fact]
    public async Task CopyAsync_WhenDestinationIsFull_EnforcesTheDatabaseLimitWithoutPartialWrites()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var seeded = await SeedAsync(
            factory,
            false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            database.Wishes.AddRange(Enumerable.Range(
                    1,
                    1000)
                .Select(position => new Wish(
                    Guid.CreateVersion7(),
                    seeded.Destination.Id,
                    "Existing wish",
                    null,
                    null,
                    null,
                    position)));
            await database.SaveChangesAsync(cancellationToken);
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE public.wish_position_sequences SET next_position = 1000 WHERE wishlist_id = {seeded.Destination.Id}",
                cancellationToken);
        }

        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seeded.Destination.OwnerId,
            cancellationToken);

        // Act
        using var response = await CopyAsync(
            client,
            seeded.Destination.Id,
            seeded.Link.Id,
            seeded.Source.Id,
            seeded.Secret);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1000,
            await context.Wishes.CountAsync(
                wish => wish.WishlistId == seeded.Destination.Id,
                cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyAsync_WhenSourceIsAccessible_CreatesAnIndependentWish(
        bool withImage)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var seeded = await SeedAsync(
            factory,
            withImage);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seeded.Destination.OwnerId,
            cancellationToken);

        // Act
        using var response = await CopyAsync(
            client,
            seeded.Destination.Id,
            seeded.Link.Id,
            seeded.Source.Id,
            seeded.Secret);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        var id = body.GetProperty("id").GetGuid();
        Assert.NotEqual(
            seeded.Source.Id,
            id);
        Assert.Equal(
            seeded.Source.Name,
            body.GetProperty("name").GetString());
        Assert.Equal(
            seeded.Source.Note,
            body.GetProperty("note").GetString());
        Assert.Equal(
            seeded.Source.Quantity,
            body.GetProperty("quantity").GetInt32());
        Assert.Equal(
            seeded.Source.Price,
            body.GetProperty("price").GetDecimal());
        Assert.Equal(
            seeded.Source.Url,
            body.GetProperty("url").GetString());
        Assert.False(body.GetProperty("isFavorite").GetBoolean());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal(
            $"/api/v1/wishlists/{seeded.Destination.Id}/wishes/{id}",
            response.Headers.Location?.AbsolutePath);

        using var read = await client.GetAsync(
            response.Headers.Location,
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            read.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var copied = await database.Wishes
            .AsNoTracking()
            .SingleAsync(
                wish => wish.Id == id,
                cancellationToken);
        Assert.False(await database.GiftReservations.AnyAsync(
            reservation => reservation.WishId == id,
            cancellationToken));

        if (withImage)
        {
            Assert.NotNull(copied.ImageId);
            Assert.NotEqual(
                seeded.Source.ImageId,
                copied.ImageId);
            var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
            await using var image = await store.OpenReadAsync(
                copied.ImageId.Value,
                cancellationToken);
            Assert.NotNull(image);
            using var content = new MemoryStream();
            await image.CopyToAsync(
                content,
                cancellationToken);
            Assert.Equal(
                seeded.Source.ImageContentHash,
                SHA256.HashData(content.ToArray()));
            // Removing the original file must never invalidate the independent destination.
            await store.DeleteAsync(
                seeded.Source.ImageId.GetValueOrDefault(),
                cancellationToken);
            await using var independent = await store.OpenReadAsync(
                copied.ImageId.Value,
                cancellationToken);
            Assert.NotNull(independent);
        }
        else
        {
            Assert.Null(copied.ImageId);
            Assert.Equal(
                JsonValueKind.Null,
                body.GetProperty("imageUrl").ValueKind);
        }
    }

    [Theory]
    [InlineData("secret", HttpStatusCode.NotFound)]
    [InlineData("wish", HttpStatusCode.NotFound)]
    [InlineData("link", HttpStatusCode.NotFound)]
    [InlineData("sourceArchived", HttpStatusCode.NotFound)]
    [InlineData("sourceSuspended", HttpStatusCode.NotFound)]
    [InlineData("destinationArchived", HttpStatusCode.Conflict)]
    [InlineData("destinationSuspended", HttpStatusCode.Conflict)]
    [InlineData("otherOwner", HttpStatusCode.NotFound)]
    public async Task CopyAsync_WhenAccessIsUnavailable_DoesNotCreateAnyWish(
        string scenario,
        HttpStatusCode expectedStatus)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var seeded = await SeedAsync(
            factory,
            false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var sourceList = await database.Wishlists.SingleAsync(
                list => list.Id == seeded.Source.WishlistId,
                cancellationToken);
            var destination = await database.Wishlists.SingleAsync(
                list => list.Id == seeded.Destination.Id,
                cancellationToken);

            if (scenario == "sourceArchived")
                sourceList.SetArchived(true);

            if (scenario == "sourceSuspended")
                sourceList.Moderate(
                    true,
                    "Test moderation",
                    TimeProvider.System.GetUtcNow().UtcDateTime);

            if (scenario == "destinationArchived")
                destination.SetArchived(true);

            if (scenario == "destinationSuspended")
                destination.Moderate(
                    true,
                    "Test moderation",
                    TimeProvider.System.GetUtcNow().UtcDateTime);
            await database.SaveChangesAsync(cancellationToken);
        }

        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            scenario == "otherOwner" ? seeded.LinkOwner : seeded.Destination.OwnerId,
            cancellationToken);

        // Act
        using var response = await CopyAsync(
            client,
            seeded.Destination.Id,
            scenario == "link" ? Guid.CreateVersion7() : seeded.Link.Id,
            scenario == "wish" ? Guid.CreateVersion7() : seeded.Source.Id,
            scenario == "secret" ? "invalid" : seeded.Secret);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await context.Wishes.AnyAsync(
            wish => wish.WishlistId == seeded.Destination.Id,
            cancellationToken));
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.ServiceUnavailable)]
    [InlineData("corrupt", HttpStatusCode.ServiceUnavailable)]
    [InlineData("oversized", HttpStatusCode.ServiceUnavailable)]
    [InlineData("read", HttpStatusCode.ServiceUnavailable)]
    [InlineData("write", HttpStatusCode.ServiceUnavailable)]
    [InlineData("marker", HttpStatusCode.Created)]
    public async Task CopyAsync_WhenImageStorageFails_PreservesAtomicCreationAndPendingRecovery(
        string scenario,
        HttpStatusCode expectedStatus)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var imageId = Guid.CreateVersion7();
        var content = new byte[]
        {
            1,
            2,
            3
        };
        var imageStoreMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        var stored = content;

        if (scenario == "corrupt")
            stored = [9];

        if (scenario == "oversized")
            stored = new byte[10 * 1024 * 1024 + 1];

        var streamMock = new Mock<Stream>(MockBehavior.Strict);

        if (scenario == "read")
        {
            streamMock.Setup(stream => stream.ReadAsync(
                    It.IsAny<Memory<byte>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Test read failure."));
            streamMock.Setup(stream => stream.DisposeAsync())
                .Returns(ValueTask.CompletedTask);
        }

        Stream? stream = scenario == "missing" ? null : new MemoryStream(stored);

        if (scenario == "read")
            stream = streamMock.Object;

        imageStoreMock.Setup(store => store.OpenReadAsync(
                imageId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);
        var writes = scenario is "write" or "marker";

        if (writes)
        {
            var setup = imageStoreMock.Setup(store => store.WritePendingAsync(
                It.Is<Guid>(id => id.Version == 7 && id != imageId),
                It.Is<ReadOnlyMemory<byte>>(bytes => bytes.ToArray().SequenceEqual(content)),
                It.IsAny<CancellationToken>()));

            if (scenario == "write")
                setup.ThrowsAsync(new GiftImageStorageUnavailableException(new IOException("Test write failure.")));
            else
                setup.Returns(Task.CompletedTask);
        }

        if (scenario == "marker")
        {
            imageStoreMock.Setup(store => store.MarkCommittedAsync(
                    It.Is<Guid>(id => id.Version == 7 && id != imageId),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new GiftImageStorageUnavailableException(new IOException("Test marker failure.")));
        }

        await using var factory = await CreateFactoryAsync(services =>
        {
            services.RemoveAll<IGiftImageStore>();
            services.AddSingleton(imageStoreMock.Object);
        });
        var seeded = await SeedAsync(
            factory,
            false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var source = await database.Wishes.SingleAsync(
                wish => wish.Id == seeded.Source.Id,
                cancellationToken);
            source.ReplaceImage(
                imageId,
                SHA256.HashData(content));
            await database.SaveChangesAsync(cancellationToken);
        }

        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seeded.Destination.OwnerId,
            cancellationToken);

        // Act
        using var response = await CopyAsync(
            client,
            seeded.Destination.Id,
            seeded.Link.Id,
            seeded.Source.Id,
            seeded.Secret);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            expectedStatus == HttpStatusCode.Created ? 1 : 0,
            await context.Wishes.CountAsync(
                wish => wish.WishlistId == seeded.Destination.Id,
                cancellationToken));
        imageStoreMock.Verify(store => store.OpenReadAsync(
                imageId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        if (writes)
        {
            imageStoreMock.Verify(store => store.WritePendingAsync(
                    It.Is<Guid>(id => id.Version == 7 && id != imageId),
                    It.Is<ReadOnlyMemory<byte>>(bytes => bytes.ToArray().SequenceEqual(content)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        if (scenario == "marker")
        {
            imageStoreMock.Verify(store => store.MarkCommittedAsync(
                    It.Is<Guid>(id => id.Version == 7 && id != imageId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        imageStoreMock.VerifyNoOtherCalls();

        if (scenario == "read")
        {
            streamMock.Verify(stream => stream.ReadAsync(
                    It.IsAny<Memory<byte>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            streamMock.Verify(
                stream => stream.DisposeAsync(),
                Times.Once);
            streamMock.VerifyNoOtherCalls();
        }
    }

    [Fact]
    public async Task CopyAsync_WhenCommitAcknowledgementIsLost_DoesNotReplayTheCommittedCopy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateFactoryAsync(services =>
        {
            services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor));
        });
        var seeded = await SeedAsync(
            factory,
            false);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            seeded.Destination.OwnerId,
            cancellationToken);
        interceptor.Arm();

        // Act
        using var response = await CopyAsync(
            client,
            seeded.Destination.Id,
            seeded.Link.Id,
            seeded.Source.Id,
            seeded.Secret);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        using var read = await client.GetAsync(
            $"/api/v1/wishlists/{seeded.Destination.Id}/wishes",
            cancellationToken);
        var body = await read.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Single(body.GetProperty("wishes").EnumerateArray());
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("state")]
    [InlineData("constraint")]
    [InlineData("inner")]
    [InlineData("timeout")]
    public async Task CopyAsync_WhenPersistenceFails_TranslatesOnlyRecognizedFailuresAndRollsBack(
        string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        Exception failure = new DbUpdateException(
            "Test save failure.",
            new PostgresException(
                "Test constraint failure.",
                "ERROR",
                "ERROR",
                scenario == "state" ? PostgresErrorCodes.UniqueViolation : PostgresErrorCodes.CheckViolation,
                constraintName: scenario == "constraint" ? "other_constraint" : "ck_wish_position_sequences_current_count_limit"));

        if (scenario == "inner")
            failure = new DbUpdateException("Test save failure.");

        if (scenario == "timeout")
            failure = new TimeoutException("Test persistence timeout.");

        var unitOfWorkMock = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unitOfWorkMock.Setup(unit => unit.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(failure);
        await using var factory = await CreateFactoryAsync(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton(unitOfWorkMock.Object);
        });
        var seeded = await SeedAsync(
            factory,
            false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IWishCopyService>();

            // Act
            var exception = await Record.ExceptionAsync(() => service.CopyAsync(
                Guid.CreateVersion7(),
                seeded.Destination.OwnerId,
                seeded.Destination.Id,
                seeded.Link.Id,
                seeded.Source.Id,
                seeded.Secret,
                cancellationToken));

            // Assert
            if (scenario == "limit")
                Assert.IsType<WishLimitReachedException>(exception);

            if (scenario == "timeout")
                Assert.IsType<DependencyUnavailableException>(exception);

            if (scenario is not ("limit" or "timeout"))
                Assert.Same(failure, exception);
        }

        await using var verification = factory.Services.CreateAsyncScope();
        var database = verification.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await database.Wishes.AnyAsync(
            wish => wish.WishlistId == seeded.Destination.Id,
            cancellationToken));
        unitOfWorkMock.Verify(
            unit => unit.SaveChangesAsync(cancellationToken),
            Times.Once);
        unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CopyAsync_WhenOwnerWasDeleted_RejectsBeforeCreatingAWish()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var seeded = await SeedAsync(
            factory,
            false);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IWishCopyService>();

        // Act
        var action = () => service.CopyAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            seeded.Destination.Id,
            seeded.Link.Id,
            seeded.Source.Id,
            seeded.Secret,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
    }

    [Fact]
    public async Task CopyAsync_WhenMembersCopyAcrossTheSameParents_BothCopiesCommitWithoutInterlocking()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var seeded = await SeedAsync(
            factory,
            false);
        var reverseWishId = Guid.CreateVersion7();
        Guid reverseLinkId;
        string reverseSecret;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var token = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>().Create();
            reverseLinkId = Guid.CreateVersion7();
            reverseSecret = token.Secret;
            database.WishlistShareLinks.Add(new WishlistShareLink(
                reverseLinkId,
                seeded.Destination.Id,
                token.SecretHash,
                token.ProtectedSecret));
            database.Wishes.Add(new Wish(
                reverseWishId,
                seeded.Destination.Id,
                "Reverse source",
                null,
                null,
                null,
                1));
            await database.SaveChangesAsync(cancellationToken);
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE public.wish_position_sequences SET next_position = 1
                WHERE wishlist_id = {seeded.Destination.Id}
                """,
                cancellationToken);
        }

        using var first = await AuthenticationTestData.CreateClientAsync(
            factory,
            seeded.Destination.OwnerId,
            cancellationToken);
        using var second = await AuthenticationTestData.CreateClientAsync(
            factory,
            seeded.LinkOwner,
            cancellationToken);

        // Act
        var responses = await Task.WhenAll(
            CopyAsync(
                first,
                seeded.Destination.Id,
                seeded.Link.Id,
                seeded.Source.Id,
                seeded.Secret),
            CopyAsync(
                second,
                seeded.Source.WishlistId,
                reverseLinkId,
                reverseWishId,
                reverseSecret));

        // Assert
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            secondResponse.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            4,
            await context.Wishes.CountAsync(cancellationToken));
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: configureServices);
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task<(Wishlist Destination, Wish Source, WishlistShareLink Link, string Secret, Guid LinkOwner)> SeedAsync(
        PostgreSqlApiFactory factory,
        bool withImage)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var sourceOwner = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            DisplayName = "Source",
            EmailConfirmed = true,
            UserName = "source@example.test",
            NormalizedUserName = "SOURCE@EXAMPLE.TEST",
            Email = "source@example.test",
            NormalizedEmail = "SOURCE@EXAMPLE.TEST",
            SecurityStamp = Guid.CreateVersion7().ToString()
        };
        var destinationOwner = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            DisplayName = "Destination",
            EmailConfirmed = true,
            UserName = "destination@example.test",
            NormalizedUserName = "DESTINATION@EXAMPLE.TEST",
            Email = "destination@example.test",
            NormalizedEmail = "DESTINATION@EXAMPLE.TEST",
            SecurityStamp = Guid.CreateVersion7().ToString()
        };
        database.Users.AddRange(
            sourceOwner,
            destinationOwner);
        var sourceList = new Wishlist(
            Guid.CreateVersion7(),
            sourceOwner.Id,
            "Source",
            "SOURCE",
            WishlistOccasion.Birthday,
            null,
            null);
        var destination = new Wishlist(
            Guid.CreateVersion7(),
            destinationOwner.Id,
            "Destination",
            "DESTINATION",
            WishlistOccasion.Other,
            null,
            null);
        database.Wishlists.AddRange(
            sourceList,
            destination);
        var token = scope.ServiceProvider.GetRequiredService<IWishlistShareTokenService>().Create();
        var link = new WishlistShareLink(
            Guid.CreateVersion7(),
            sourceList.Id,
            token.SecretHash,
            token.ProtectedSecret);
        database.WishlistShareLinks.Add(link);
        var source = new Wish(
            Guid.CreateVersion7(),
            sourceList.Id,
            "Original wish",
            "Original note",
            "https://example.com/product",
            12.34m,
            1,
            3);
        source.SetFavorite(true);

        if (withImage)
        {
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
            source.ReplaceImage(
                imageId,
                SHA256.HashData(content));
        }

        database.Wishes.Add(source);
        var participant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            sourceList.Id,
            destinationOwner.Id);
        database.WishlistParticipants.Add(participant);
        database.GiftReservations.Add(new GiftReservation(
            Guid.CreateVersion7(),
            sourceList.Id,
            source.Id,
            participant.Id,
            1));
        await database.SaveChangesAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE public.wish_position_sequences SET next_position = 1
            WHERE wishlist_id = {sourceList.Id}
            """,
            cancellationToken);

        return (destination, source, link, token.Secret, sourceOwner.Id);
    }

    private static async Task<HttpResponseMessage> CopyAsync(
        HttpClient client,
        Guid wishlistId,
        Guid linkId,
        Guid wishId,
        string secret)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/wishlists/{wishlistId}/wishes/copies")
        {
            Content = JsonContent.Create(new
            {
                sourceShareLinkId = linkId,
                sourceWishId = wishId
            })
        };
        request.Headers.Add(
            "X-MonKado-Share-Token",
            secret);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await GetCsrfTokenAsync(
                client,
                TestContext.Current.CancellationToken));

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        return body.GetProperty("token").GetString() ?? throw new InvalidOperationException("Missing CSRF token.");
    }
}
