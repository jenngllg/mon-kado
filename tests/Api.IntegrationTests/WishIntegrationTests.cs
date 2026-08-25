using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset _referenceTime = new(
        2026,
        8,
        25,
        10,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task ReorderAsync_WhenOrderChanges_ReusesExistingSlotsAndPersistsCompleteOrder()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste ordonnée");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var firstCreation = await CreateWishAsync(
            client,
            wishlist.Id,
            "Premier");
        using var removedCreation = await CreateWishAsync(
            client,
            wishlist.Id,
            "Supprimé");
        using var thirdCreation = await CreateWishAsync(
            client,
            wishlist.Id,
            "Troisième");
        var firstId = await ReadWishIdAsync(firstCreation);
        var removedId = await ReadWishIdAsync(removedCreation);
        var thirdId = await ReadWishIdAsync(thirdCreation);
        using var deletion = await DeleteWishAsync(
            client,
            wishlist.Id,
            removedId,
            removedCreation.Headers.ETag?.Tag);
        using var fourthCreation = await CreateWishAsync(
            client,
            wishlist.Id,
            "Quatrième");
        var fourthId = await ReadWishIdAsync(fourthCreation);
        using var collectionBefore = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}/wishes",
            TestContext.Current.CancellationToken);

        // Act
        using var reorder = await ReorderWishesAsync(
            client,
            wishlist.Id,
            [
                firstId,
                fourthId,
                thirdId
            ],
            collectionBefore.Headers.ETag?.Tag);
        using var collectionAfter = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}/wishes",
            TestContext.Current.CancellationToken);
        var reorderBody = await reorder.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var collectionBody = await collectionAfter.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var reorderError = reorder.StatusCode == HttpStatusCode.OK
            ? null
            : await reorder.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            deletion.StatusCode);
        Assert.True(
            reorder.StatusCode == HttpStatusCode.OK,
            reorderError);
        Assert.NotEqual(
            collectionBefore.Headers.ETag?.Tag,
            reorder.Headers.ETag?.Tag);
        Assert.Equal(
            reorder.Headers.ETag?.Tag,
            collectionAfter.Headers.ETag?.Tag);
        var reorderedWishes = reorderBody
            .GetProperty("wishes")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            [firstId, fourthId, thirdId],
            reorderedWishes.Select(wish => wish.GetProperty("id").GetGuid()));
        Assert.Equal(
            [1L, 3L, 4L],
            reorderedWishes.Select(wish => wish.GetProperty("position").GetInt64()));
        Assert.All(
            reorderedWishes,
            wish => Assert.False(string.IsNullOrWhiteSpace(
                wish.GetProperty("entityTag").GetString())));
        Assert.Equal(
            [firstId, fourthId, thirdId],
            collectionBody
                .GetProperty("wishes")
                .EnumerateArray()
                .Select(wish => wish.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task ReorderAsync_WhenOrderIsUnchanged_PreservesCollectionAndItemEntityTags()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste stable");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creation = await CreateWishAsync(
            client,
            wishlist.Id,
            "Stable");
        var wishId = await ReadWishIdAsync(creation);
        using var collection = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}/wishes",
            TestContext.Current.CancellationToken);
        var collectionBody = await collection.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var itemEntityTag = collectionBody
            .GetProperty("wishes")[0]
            .GetProperty("entityTag")
            .GetString();

        // Act
        using var reorder = await ReorderWishesAsync(
            client,
            wishlist.Id,
            [wishId],
            collection.Headers.ETag?.Tag);
        var reorderBody = await reorder.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            reorder.StatusCode);
        Assert.Equal(
            collection.Headers.ETag?.Tag,
            reorder.Headers.ETag?.Tag);
        Assert.Equal(
            itemEntityTag,
            reorderBody
                .GetProperty("wishes")[0]
                .GetProperty("entityTag")
                .GetString());
    }

    [Fact]
    public async Task ReorderAsync_WhenCollectionChanged_ReturnsPreconditionFailed()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste concurrente");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var emptyCollection = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}/wishes",
            TestContext.Current.CancellationToken);
        using var creation = await CreateWishAsync(
            client,
            wishlist.Id,
            "Concurrent");
        var wishId = await ReadWishIdAsync(creation);

        // Act
        using var response = await ReorderWishesAsync(
            client,
            wishlist.Id,
            [wishId],
            emptyCollection.Headers.ETag?.Tag);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.WishOrderVersionConflict,
            error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ReorderAsync_WhenMembershipIsIncomplete_ReturnsConflictWithoutChangingOrder()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste incomplète");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creation = await CreateWishAsync(
            client,
            wishlist.Id,
            "Présent");
        using var collection = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}/wishes",
            TestContext.Current.CancellationToken);

        // Act
        using var response = await ReorderWishesAsync(
            client,
            wishlist.Id,
            [],
            collection.Headers.ETag?.Tag);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.WishOrderConflict,
            error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ReorderAsync_WhenCommitAcknowledgementIsLost_ReturnsCommittedOrder()
    {
        // Arrange
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateFactoryAsync(services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor));
        });
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste réordonnée après commit ambigu");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var firstCreation = await CreateWishAsync(
            client,
            wishlist.Id,
            "Premier");
        using var secondCreation = await CreateWishAsync(
            client,
            wishlist.Id,
            "Deuxième");
        var firstId = await ReadWishIdAsync(firstCreation);
        var secondId = await ReadWishIdAsync(secondCreation);
        using var collectionBefore = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}/wishes",
            TestContext.Current.CancellationToken);
        interceptor.Arm();

        // Act
        using var reorder = await ReorderWishesAsync(
            client,
            wishlist.Id,
            [secondId, firstId],
            collectionBefore.Headers.ETag?.Tag);
        using var collectionAfter = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}/wishes",
            TestContext.Current.CancellationToken);
        var body = await collectionAfter.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            reorder.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            collectionAfter.StatusCode);
        Assert.Equal(
            [secondId, firstId],
            body
                .GetProperty("wishes")
                .EnumerateArray()
                .Select(wish => wish.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task CreateAsync_WhenRequestIsValid_PersistsAndRetrievesNestedWishWithoutUpdatingParent()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste anniversaire");
        var parentVersion = wishlist.Version;
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);

        // Act
        using var creationResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "  Cafe\u0301  ");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();
        using var retrievalResponse = await client.GetAsync(
            creationResponse.Headers.Location,
            TestContext.Current.CancellationToken);
        var retrieved = await retrievalResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var storedWish = await GetWishAsync(
            factory,
            wishlist.Id,
            wishId);
        var storedParent = await GetWishlistAsync(
            factory,
            wishlist.Id);
        var nextPosition = await GetNextPositionAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            creationResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            retrievalResponse.StatusCode);
        Assert.Equal(
            wishId,
            retrieved.GetProperty("id").GetGuid());
        Assert.Equal(
            wishlist.Id,
            storedWish.WishlistId);
        Assert.Equal(
            7,
            storedWish.Id.Version);
        Assert.Equal(
            "Café",
            storedWish.Name);
        Assert.Equal(
            "Édition blanche",
            storedWish.Note);
        Assert.Equal(
            "https://example.com/gift",
            storedWish.Url);
        Assert.Equal(
            12.34m,
            storedWish.Price);
        Assert.Equal(
            1,
            storedWish.Position);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            storedWish.CreatedAt);
        Assert.Null(storedWish.UpdatedAt);
        Assert.NotEqual(
            0u,
            storedWish.Version);
        Assert.Equal(
            1,
            nextPosition);
        Assert.Equal(
            parentVersion,
            storedParent.Version);
        Assert.Null(storedParent.UpdatedAt);
        Assert.Equal(
            creationResponse.Headers.ETag?.Tag,
            retrievalResponse.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task UpdateAsync_WhenRequestIsValid_ReplacesEditableValuesAndPersistsThem()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste anniversaire");
        var parentVersion = wishlist.Version;
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau initial");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();

        // Act
        using var updateResponse = await UpdateWishAsync(
            client,
            wishlist.Id,
            wishId,
            new
            {
                name = "  Cafe\u0301 premium  ",
                note = "   ",
                url = (string?)null,
                price = (decimal?)null
            },
            creationResponse.Headers.ETag?.Tag);
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        using var retrievalResponse = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}/wishes/{wishId}",
            TestContext.Current.CancellationToken);
        var retrieved = await retrievalResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var storedWish = await GetWishAsync(
            factory,
            wishlist.Id,
            wishId);
        var storedParent = await GetWishlistAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            updateResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            retrievalResponse.StatusCode);
        Assert.Equal(
            "Café premium",
            updated.GetProperty("name").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            updated.GetProperty("note").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            updated.GetProperty("url").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            updated.GetProperty("price").ValueKind);
        Assert.Equal(
            1,
            updated.GetProperty("position").GetInt64());
        Assert.NotEqual(
            creationResponse.Headers.ETag?.Tag,
            updateResponse.Headers.ETag?.Tag);
        Assert.Equal(
            updateResponse.Headers.ETag?.Tag,
            retrievalResponse.Headers.ETag?.Tag);
        Assert.Equal(
            updated.GetProperty("name").GetString(),
            retrieved.GetProperty("name").GetString());
        Assert.Equal(
            "Café premium",
            storedWish.Name);
        Assert.Null(storedWish.Note);
        Assert.Null(storedWish.Url);
        Assert.Null(storedWish.Price);
        Assert.Equal(
            1,
            storedWish.Position);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            storedWish.UpdatedAt);
        Assert.Equal(
            parentVersion,
            storedParent.Version);
        Assert.Null(storedParent.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_WhenValuesAreUnchanged_PreservesVersionAndUpdatedAt()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste anniversaire");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Café");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();

        // Act
        using var updateResponse = await UpdateWishAsync(
            client,
            wishlist.Id,
            wishId,
            new
            {
                name = "Café",
                note = "Édition blanche",
                url = "https://example.com/gift",
                price = 12.34m
            },
            creationResponse.Headers.ETag?.Tag);
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var storedWish = await GetWishAsync(
            factory,
            wishlist.Id,
            wishId);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            updateResponse.StatusCode);
        Assert.Equal(
            creationResponse.Headers.ETag?.Tag,
            updateResponse.Headers.ETag?.Tag);
        Assert.Equal(
            JsonValueKind.Null,
            updated.GetProperty("updatedAt").ValueKind);
        Assert.Null(storedWish.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_WhenEntityTagIsStale_ReturnsPreconditionFailedWithoutChangingWish()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste anniversaire");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau initial");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();

        // Act
        using var updateResponse = await UpdateWishAsync(
            client,
            wishlist.Id,
            wishId,
            new
            {
                name = "Cadeau modifié",
                note = (string?)null,
                url = (string?)null,
                price = (decimal?)null
            },
            "\"00000000\"");
        var error = await updateResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        var storedWish = await GetWishAsync(
            factory,
            wishlist.Id,
            wishId);

        // Assert
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            updateResponse.StatusCode);
        Assert.Equal(
            ErrorCodes.WishVersionConflict,
            error?.ErrorCode);
        Assert.Equal(
            "Cadeau initial",
            storedWish.Name);
        Assert.Equal(
            "Édition blanche",
            storedWish.Note);
        Assert.Null(storedWish.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_WhenWishBelongsToDifferentOwnedParent_ReturnsWishNotFound()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var sourceWishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste source");
        var targetWishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste cible");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            sourceWishlist.Id,
            "Cadeau");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Act
        using var updateResponse = await UpdateWishAsync(
            client,
            targetWishlist.Id,
            created.GetProperty("id").GetGuid(),
            new
            {
                name = "Cadeau modifié"
            },
            creationResponse.Headers.ETag?.Tag);
        var error = await updateResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            updateResponse.StatusCode);
        Assert.Equal(
            ErrorCodes.WishNotFound,
            error?.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenTwoEqualNamesAreCreatedConcurrently_AllocatesDistinctSequentialPositions()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste parallèle");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);

        // Act
        var firstTask = CreateWishAsync(
            client,
            wishlist.Id,
            "Même cadeau");
        var secondTask = CreateWishAsync(
            client,
            wishlist.Id,
            "Même cadeau");
        var responses = await Task.WhenAll(
            firstTask,
            secondTask);
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var storedWishes = await GetWishesAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.All(
            responses,
            response => Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode));
        Assert.Equal(
            2,
            storedWishes.Count);
        Assert.All(
            storedWishes,
            wish => Assert.Equal(
                "Même cadeau",
                wish.Name));
        Assert.Equal(
            [
                1L,
                2L
            ],
            storedWishes
                .Select(wish => wish.Position)
                .OrderBy(position => position));
    }

    [Fact]
    public async Task GetAsync_WhenWishBelongsToDifferentParent_ReturnsNotFound()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var firstWishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Première liste");
        var secondWishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Deuxième liste");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            firstWishlist.Id,
            "Cadeau privé");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{secondWishlist.Id}/wishes/{wishId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishNotFound,
            error.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenCommitAcknowledgementIsLost_ReturnsSingleCommittedWish()
    {
        // Arrange
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateFactoryAsync(services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor));
        });
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste ambiguë");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        interceptor.Arm();

        // Act
        using var response = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau ambigu");
        var storedWishes = await GetWishesAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        var storedWish = Assert.Single(storedWishes);
        Assert.Equal(
            "Cadeau ambigu",
            storedWish.Name);
    }

    [Fact]
    public async Task CreateAsync_WhenAuthenticatedMemberWasDeleted_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste supprimée");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        await DeleteMemberAsync(
            factory,
            owner.Id);

        // Act
        using var response = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau");

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenRequestIsValid_RemovesWishWithoutUpdatingParent()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste suppression");
        var parentVersion = wishlist.Version;
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau supprimé");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();

        // Act
        using var deletionResponse = await DeleteWishAsync(
            client,
            wishlist.Id,
            wishId,
            creationResponse.Headers.ETag?.Tag);
        using var retrievalResponse = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}/wishes/{wishId}",
            TestContext.Current.CancellationToken);
        var storedWishes = await GetWishesAsync(
            factory,
            wishlist.Id);
        var storedParent = await GetWishlistAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            deletionResponse.StatusCode);
        Assert.True(deletionResponse.Headers.CacheControl?.NoStore);
        Assert.Null(deletionResponse.Headers.ETag);
        Assert.Equal(
            string.Empty,
            await deletionResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            HttpStatusCode.NotFound,
            retrievalResponse.StatusCode);
        Assert.Empty(storedWishes);
        Assert.Equal(
            parentVersion,
            storedParent.Version);
        Assert.Null(storedParent.UpdatedAt);
    }

    [Fact]
    public async Task DeleteAsync_WhenRepeated_ReturnsWishNotFound()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste double suppression");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau supprimé deux fois");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();
        using var firstResponse = await DeleteWishAsync(
            client,
            wishlist.Id,
            wishId,
            creationResponse.Headers.ETag?.Tag);

        // Act
        using var secondResponse = await DeleteWishAsync(
            client,
            wishlist.Id,
            wishId,
            creationResponse.Headers.ETag?.Tag);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            secondResponse.StatusCode);
        var error = await secondResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishNotFound,
            error.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenEntityTagIsStale_ReturnsPreconditionFailedWithoutDeletingWish()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste concurrence suppression");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau initial");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();
        var staleEntityTag = creationResponse.Headers.ETag?.Tag;
        using var updateResponse = await UpdateWishAsync(
            client,
            wishlist.Id,
            wishId,
            new
            {
                name = "Cadeau modifié"
            },
            staleEntityTag);

        // Act
        using var deletionResponse = await DeleteWishAsync(
            client,
            wishlist.Id,
            wishId,
            staleEntityTag);
        var storedWish = await GetWishAsync(
            factory,
            wishlist.Id,
            wishId);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            updateResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            deletionResponse.StatusCode);
        Assert.Equal(
            "Cadeau modifié",
            storedWish.Name);
    }

    [Fact]
    public async Task DeleteAsync_WhenWishBelongsToDifferentOwnedParent_ReturnsWishNotFound()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var firstWishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Première liste suppression");
        var secondWishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Deuxième liste suppression");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            firstWishlist.Id,
            "Cadeau privé");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();

        // Act
        using var deletionResponse = await DeleteWishAsync(
            client,
            secondWishlist.Id,
            wishId,
            creationResponse.Headers.ETag?.Tag);
        var storedWish = await GetWishAsync(
            factory,
            firstWishlist.Id,
            wishId);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            deletionResponse.StatusCode);
        var error = await deletionResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishNotFound,
            error.ErrorCode);
        Assert.Equal(
            "Cadeau privé",
            storedWish.Name);
    }

    [Fact]
    public async Task DeleteAsync_WhenMiddleWishIsRemoved_PreservesPositionsAndSequence()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste positions");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var firstResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Premier cadeau");
        using var secondResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Deuxième cadeau");
        using var thirdResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Troisième cadeau");
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var secondWishId = second.GetProperty("id").GetGuid();
        using var deletionResponse = await DeleteWishAsync(
            client,
            wishlist.Id,
            secondWishId,
            secondResponse.Headers.ETag?.Tag);

        // Act
        using var fourthResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Quatrième cadeau");
        var storedWishes = await GetWishesAsync(
            factory,
            wishlist.Id);
        var nextPosition = await GetNextPositionAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            deletionResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            fourthResponse.StatusCode);
        Assert.Equal(
            [
                1L,
                3L,
                4L
            ],
            storedWishes
                .Select(wish => wish.Position)
                .OrderBy(position => position));
        Assert.Equal(
            4,
            nextPosition);
    }

    [Fact]
    public async Task DeleteAsync_WhenCommitAcknowledgementIsLost_ReturnsNoContentAfterDeletion()
    {
        // Arrange
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateFactoryAsync(services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor));
        });
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste suppression ambiguë");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau ambigu");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();
        interceptor.Arm();

        // Act
        using var deletionResponse = await DeleteWishAsync(
            client,
            wishlist.Id,
            wishId,
            creationResponse.Headers.ETag?.Tag);
        var storedWishes = await GetWishesAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            deletionResponse.StatusCode);
        Assert.Empty(storedWishes);
    }

    [Fact]
    public async Task AllocatePositionAsync_WhenConnectionIsAlreadyOpen_KeepsConnectionOpen()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste connexion ouverte");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IWishRepository>();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Act
        var position = await repository.AllocatePositionAsync(
            wishlist.Id,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1,
            position);
        Assert.Equal(
            ConnectionState.Open,
            context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenUrlContainsCredentials_RejectsCredentials()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste URL");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.Wishes.Add(new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "URL interdite",
            null,
            "https://user:password@example.com/gift",
            null,
            1));

        // Act
        var action = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(action);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenUrlContainsAtInPath_PersistsWish()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste URL valide");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.Wishes.Add(new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "URL autorisée",
            null,
            "https://example.com/path@value",
            null,
            1));

        // Act
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var url = await context.Wishes
            .AsNoTracking()
            .Select(wish => wish.Url)
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://example.com/path@value",
            url);
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            new FixedTimeProvider(_referenceTime),
            configureServices: configureServices);
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
            Email = "owner@example.fr",
            UserName = "owner@example.fr",
            DisplayName = "Jenn",
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

    private static async Task<HttpResponseMessage> CreateWishAsync(
        HttpClient client,
        Guid wishlistId,
        string name)
    {
        return await client.PostAsJsonAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes",
            new
            {
                name,
                note = "  Édition blanche  ",
                url = "  https://example.com/gift  ",
                price = 12.34m
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> UpdateWishAsync(
        HttpClient client,
        Guid wishlistId,
        Guid wishId,
        object body,
        string? entityTag)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}")
        {
            Content = JsonContent.Create(body)
        };

        if (entityTag is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> ReorderWishesAsync(
        HttpClient client,
        Guid wishlistId,
        IReadOnlyCollection<Guid> wishIds,
        string? entityTag)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/wishlists/{wishlistId}/wishes")
        {
            Content = JsonContent.Create(new
            {
                wishIds
            })
        };

        if (entityTag is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> ReadWishIdAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        return body.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> DeleteWishAsync(
        HttpClient client,
        Guid wishlistId,
        Guid wishId,
        string? entityTag)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}");

        if (entityTag is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
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

    private static async Task<Wish> GetWishAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId,
        Guid wishId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Wishes
            .AsNoTracking()
            .SingleAsync(
                wish => wish.WishlistId == wishlistId && wish.Id == wishId,
                TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyCollection<Wish>> GetWishesAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Wishes
            .AsNoTracking()
            .Where(wish => wish.WishlistId == wishlistId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Wishlist> GetWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Wishlists
            .AsNoTracking()
            .SingleAsync(
                wishlist => wishlist.Id == wishlistId,
                TestContext.Current.CancellationToken);
    }

    private static async Task<long> GetNextPositionAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT next_position FROM public.wish_position_sequences WHERE wishlist_id = @wishlist_id;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "wishlist_id";
        parameter.Value = wishlistId;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task DeleteMemberAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }
}
