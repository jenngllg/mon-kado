using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;
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
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class GiftReservationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task PutAsync_WhenCreatedAndReplaced_ExposesConsistentPublicQuantities()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (memberId, "participant@example.test", "Participant")
            ],
            wishlistId,
            wishId,
            3,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var participantClient = CreateAuthorizedClient(
            factory,
            memberId);
        var csrfToken = await GetCsrfTokenAsync(
            participantClient,
            cancellationToken);
        using var join = await JoinAsync(
            participantClient,
            shareLink.Id,
            shareLink.Secret,
            csrfToken,
            cancellationToken);

        // Act
        using var creation = await UpsertAsync(
            participantClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            csrfToken,
            1,
            null,
            cancellationToken);
        var createdBody = await creation.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var firstEntityTag = creation.Headers.ETag?.Tag;
        using var missingEntityTag = await UpsertAsync(
            participantClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            csrfToken,
            2,
            null,
            cancellationToken);
        using var replacement = await UpsertAsync(
            participantClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            csrfToken,
            2,
            firstEntityTag,
            cancellationToken);
        var replacedBody = await replacement.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var secondEntityTag = replacement.Headers.ETag?.Tag;
        using var staleReplacement = await UpsertAsync(
            participantClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            csrfToken,
            1,
            firstEntityTag,
            cancellationToken);
        using var unavailableReplacement = await UpsertAsync(
            participantClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            csrfToken,
            4,
            secondEntityTag,
            cancellationToken);
        using var currentReservation = await GetCurrentAsync(
            participantClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            cancellationToken);
        using var sharedWishlist = await GetSharedWishlistAsync(
            participantClient,
            shareLink.Id,
            shareLink.Secret,
            availableOnly: false,
            cancellationToken);
        using var historyWithShareLink = await GetHistoryAsync(
            participantClient,
            cancellationToken);
        using var rotation = await RotateShareLinkAsync(
            ownerClient,
            wishlistId,
            shareLink.EntityTag,
            cancellationToken);
        using var revocation = await RevokeShareLinkAsync(
            ownerClient,
            wishlistId,
            rotation.Headers.ETag?.Tag,
            cancellationToken);
        var reservationsAfterRevocation = await GetStoredReservationsAsync(
            factory,
            cancellationToken);
        using var historyAfterRevocation = await GetHistoryAsync(
            participantClient,
            cancellationToken);
        var wishEntityTag = await GetWishEntityTagAsync(
            ownerClient,
            wishlistId,
            wishId,
            cancellationToken);
        using var wishDeletion = await DeleteWishAsync(
            ownerClient,
            wishlistId,
            wishId,
            wishEntityTag,
            cancellationToken);
        var reservationsAfterWishDeletion = await GetStoredReservationsAsync(
            factory,
            cancellationToken);
        using var historyAfterWishDeletion = await GetHistoryAsync(
            participantClient,
            cancellationToken);
        await DeleteMemberAsync(
            factory,
            memberId,
            cancellationToken);
        var historiesAfterMemberDeletion = await GetStoredHistoriesAsync(
            factory,
            cancellationToken);
        using var deletedMemberHistoryResponse = await GetHistoryAsync(
            participantClient,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            join.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            creation.StatusCode);
        Assert.NotNull(firstEntityTag);
        Assert.Equal(
            $"http://localhost/api/v1/shared-wishlists/{shareLink.Id}/wishes/{wishId}/reservations/current",
            creation.Headers.Location?.AbsoluteUri);
        Assert.Equal(
            [
                "id",
                "wishId",
                "quantity",
                "createdAt",
                "updatedAt"
            ],
            createdBody
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            wishId,
            createdBody.GetProperty("wishId").GetGuid());
        Assert.Equal(
            1,
            createdBody.GetProperty("quantity").GetInt32());
        Assert.Equal(
            JsonValueKind.Null,
            createdBody.GetProperty("updatedAt").ValueKind);
        Assert.Equal(
            HttpStatusCode.PreconditionRequired,
            missingEntityTag.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            replacement.StatusCode);
        Assert.NotEqual(
            firstEntityTag,
            secondEntityTag);
        Assert.Equal(
            createdBody.GetProperty("id").GetGuid(),
            replacedBody.GetProperty("id").GetGuid());
        Assert.Equal(
            2,
            replacedBody.GetProperty("quantity").GetInt32());
        Assert.Equal(
            JsonValueKind.String,
            replacedBody.GetProperty("updatedAt").ValueKind);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            staleReplacement.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            unavailableReplacement.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            currentReservation.StatusCode);
        Assert.Equal(
            secondEntityTag,
            currentReservation.Headers.ETag?.Tag);
        Assert.Equal(
            HttpStatusCode.OK,
            sharedWishlist.StatusCode);
        var sharedBody = await sharedWishlist.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var sharedWish = Assert.Single(sharedBody.GetProperty("wishes").EnumerateArray());
        Assert.Equal(
            3,
            sharedWish.GetProperty("quantity").GetInt32());
        Assert.Equal(
            2,
            sharedWish.GetProperty("reservedQuantity").GetInt32());
        Assert.Equal(
            1,
            sharedWish.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(
            2,
            sharedWish.GetProperty("currentParticipantReservedQuantity").GetInt32());
        Assert.Equal(
            HttpStatusCode.OK,
            historyWithShareLink.StatusCode);
        var activeHistory = await GetSingleHistoryItemAsync(
            historyWithShareLink,
            cancellationToken);
        Assert.Equal(
            shareLink.Id,
            activeHistory.GetProperty("shareLinkId").GetGuid());
        Assert.Equal(
            2,
            activeHistory.GetProperty("quantity").GetInt32());
        Assert.Equal(
            "active",
            activeHistory.GetProperty("status").GetString());
        Assert.Equal(
            HttpStatusCode.OK,
            rotation.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            revocation.StatusCode);
        Assert.Single(reservationsAfterRevocation);
        var historyWithoutShareLink = await GetSingleHistoryItemAsync(
            historyAfterRevocation,
            cancellationToken);
        Assert.Equal(
            JsonValueKind.Null,
            historyWithoutShareLink.GetProperty("shareLinkId").ValueKind);
        Assert.Equal(
            "active",
            historyWithoutShareLink.GetProperty("status").GetString());
        Assert.Equal(
            HttpStatusCode.NoContent,
            wishDeletion.StatusCode);
        Assert.Empty(reservationsAfterWishDeletion);
        var unavailableHistory = await GetSingleHistoryItemAsync(
            historyAfterWishDeletion,
            cancellationToken);
        Assert.Equal(
            "Birthday",
            unavailableHistory.GetProperty("wishlistName").GetString());
        Assert.Equal(
            "Gift",
            unavailableHistory.GetProperty("wishName").GetString());
        Assert.Equal(
            "unavailable",
            unavailableHistory.GetProperty("status").GetString());
        Assert.Equal(
            JsonValueKind.String,
            unavailableHistory.GetProperty("endedAt").ValueKind);
        Assert.Empty(historiesAfterMemberDeletion);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            deletedMemberHistoryResponse.StatusCode);
    }

    [Fact]
    public async Task GetHistoryAsync_WhenEntriesDiffer_FiltersOrdersAndPaginatesDeterministically()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (memberId, "participant@example.test", "Participant")
            ],
            wishlistId,
            wishId,
            3,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        var createdAt = new DateTime(
            2026,
            9,
            5,
            8,
            0,
            0,
            DateTimeKind.Utc);
        var active = new GiftReservationHistory(
            Guid.CreateVersion7(),
            memberId,
            wishlistId,
            "Old wishlist name",
            wishId,
            "Old wish name",
            1,
            createdAt,
            createdAt.AddHours(1));
        var unavailable = new GiftReservationHistory(
            Guid.CreateVersion7(),
            memberId,
            Guid.CreateVersion7(),
            "Deleted wishlist",
            Guid.CreateVersion7(),
            "Deleted gift",
            2,
            createdAt,
            createdAt);
        unavailable.End(
            GiftReservationHistoryStatus.Unavailable,
            createdAt.AddHours(2));
        var cancelled = new GiftReservationHistory(
            Guid.CreateVersion7(),
            memberId,
            Guid.CreateVersion7(),
            "Cancelled wishlist",
            Guid.CreateVersion7(),
            "Cancelled gift",
            3,
            createdAt,
            createdAt);
        cancelled.End(
            GiftReservationHistoryStatus.Cancelled,
            createdAt.AddHours(3));
        await AddHistoriesAsync(
            factory,
            [
                active,
                unavailable,
                cancelled
            ],
            cancellationToken);
        using var memberClient = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var firstPageResponse = await memberClient.GetAsync(
            "/api/v1/members/current/reservations?page=1&pageSize=2",
            cancellationToken);
        using var secondPageResponse = await memberClient.GetAsync(
            "/api/v1/members/current/reservations?page=2&pageSize=2",
            cancellationToken);
        using var beyondLastPageResponse = await memberClient.GetAsync(
            "/api/v1/members/current/reservations?page=3&pageSize=2",
            cancellationToken);
        using var activeResponse = await memberClient.GetAsync(
            "/api/v1/members/current/reservations?status=active",
            cancellationToken);
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var beyondLastPage = await beyondLastPageResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var activePage = await activeResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            firstPageResponse.StatusCode);
        Assert.Equal(
            [
                cancelled.Id,
                unavailable.Id
            ],
            firstPage.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(
            2,
            firstPage.GetProperty("totalPages").GetInt32());
        Assert.Equal(
            [
                active.Id
            ],
            secondPage.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()));
        Assert.Empty(beyondLastPage.GetProperty("items").EnumerateArray());
        Assert.Equal(
            3,
            beyondLastPage.GetProperty("currentPage").GetInt32());
        Assert.Equal(
            3,
            beyondLastPage.GetProperty("totalCount").GetInt32());
        Assert.True(beyondLastPage.GetProperty("hasPreviousPage").GetBoolean());
        Assert.False(beyondLastPage.GetProperty("hasNextPage").GetBoolean());
        var activeItem = Assert.Single(activePage.GetProperty("items").EnumerateArray());
        Assert.Equal(
            1,
            activePage.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            "Birthday",
            activeItem.GetProperty("wishlistName").GetString());
        Assert.Equal(
            "Gift",
            activeItem.GetProperty("wishName").GetString());
        Assert.Equal(
            shareLink.Id,
            activeItem.GetProperty("shareLinkId").GetGuid());
    }

    [Fact]
    public async Task GetAsync_WhenAvailableOnlyIsTrue_HidesFullyReservedGiftExceptForCurrentParticipant()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var firstMemberId = Guid.CreateVersion7();
        var secondMemberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (firstMemberId, "first@example.test", "First participant"),
                (secondMemberId, "second@example.test", "Second participant")
            ],
            wishlistId,
            wishId,
            2,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var firstClient = CreateAuthorizedClient(
            factory,
            firstMemberId);
        using var secondClient = CreateAuthorizedClient(
            factory,
            secondMemberId);
        var firstCsrfToken = await GetCsrfTokenAsync(
            firstClient,
            cancellationToken);
        var secondCsrfToken = await GetCsrfTokenAsync(
            secondClient,
            cancellationToken);
        using var firstJoin = await JoinAsync(
            firstClient,
            shareLink.Id,
            shareLink.Secret,
            firstCsrfToken,
            cancellationToken);
        using var secondJoin = await JoinAsync(
            secondClient,
            shareLink.Id,
            shareLink.Secret,
            secondCsrfToken,
            cancellationToken);
        using var reservation = await UpsertAsync(
            secondClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            secondCsrfToken,
            2,
            entityTag: null,
            cancellationToken);

        // Act
        using var unavailableForFirstParticipant = await GetSharedWishlistAsync(
            firstClient,
            shareLink.Id,
            shareLink.Secret,
            availableOnly: true,
            cancellationToken);
        using var reservedBySecondParticipant = await GetSharedWishlistAsync(
            secondClient,
            shareLink.Id,
            shareLink.Secret,
            availableOnly: true,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            firstJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            secondJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            reservation.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            unavailableForFirstParticipant.StatusCode);
        var unavailableBody = await unavailableForFirstParticipant.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken);
        Assert.Empty(unavailableBody.GetProperty("wishes").EnumerateArray());
        Assert.Equal(
            HttpStatusCode.OK,
            reservedBySecondParticipant.StatusCode);
        var reservedBody = await reservedBySecondParticipant.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken);
        var reservedWish = Assert.Single(reservedBody.GetProperty("wishes").EnumerateArray());
        Assert.Equal(
            wishId,
            reservedWish.GetProperty("id").GetGuid());
        Assert.Equal(
            0,
            reservedWish.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(
            2,
            reservedWish.GetProperty("currentParticipantReservedQuantity").GetInt32());
    }

    [Fact]
    public async Task PutAsync_WhenLastUnitIsReservedConcurrently_PreventsOverReservation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var firstMemberId = Guid.CreateVersion7();
        var secondMemberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (firstMemberId, "first@example.test", "First participant"),
                (secondMemberId, "second@example.test", "Second participant")
            ],
            wishlistId,
            wishId,
            1,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var firstClient = CreateAuthorizedClient(
            factory,
            firstMemberId);
        using var secondClient = CreateAuthorizedClient(
            factory,
            secondMemberId);
        var firstCsrfToken = await GetCsrfTokenAsync(
            firstClient,
            cancellationToken);
        var secondCsrfToken = await GetCsrfTokenAsync(
            secondClient,
            cancellationToken);
        using var firstJoin = await JoinAsync(
            firstClient,
            shareLink.Id,
            shareLink.Secret,
            firstCsrfToken,
            cancellationToken);
        using var secondJoin = await JoinAsync(
            secondClient,
            shareLink.Id,
            shareLink.Secret,
            secondCsrfToken,
            cancellationToken);

        // Act
        var reservationTasks = new[]
        {
            UpsertAsync(
                firstClient,
                shareLink.Id,
                wishId,
                shareLink.Secret,
                firstCsrfToken,
                1,
                null,
                cancellationToken),
            UpsertAsync(
                secondClient,
                shareLink.Id,
                wishId,
                shareLink.Secret,
                secondCsrfToken,
                1,
                null,
                cancellationToken)
        };
        var responses = await Task.WhenAll(reservationTasks);
        var storedReservations = await GetStoredReservationsAsync(
            factory,
            cancellationToken);
        try
        {
            // Assert
            Assert.Equal(
                HttpStatusCode.Created,
                firstJoin.StatusCode);
            Assert.Equal(
                HttpStatusCode.Created,
                secondJoin.StatusCode);
            Assert.Equal(
                [
                    HttpStatusCode.Created,
                    HttpStatusCode.Conflict
                ],
                responses
                    .Select(response => response.StatusCode)
                    .Order());
            var storedReservation = Assert.Single(storedReservations);
            Assert.Equal(
                wishId,
                storedReservation.WishId);
            Assert.Equal(
                1,
                storedReservation.Quantity);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task UpdateWishAsync_WhenQuantityFallsBelowReservations_ReturnsConflictAndPreservesWish()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (memberId, "participant@example.test", "Participant")
            ],
            wishlistId,
            wishId,
            3,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var participantClient = CreateAuthorizedClient(
            factory,
            memberId);
        var csrfToken = await GetCsrfTokenAsync(
            participantClient,
            cancellationToken);
        using var join = await JoinAsync(
            participantClient,
            shareLink.Id,
            shareLink.Secret,
            csrfToken,
            cancellationToken);
        using var reservation = await UpsertAsync(
            participantClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            csrfToken,
            2,
            null,
            cancellationToken);
        var wishEntityTag = await GetWishEntityTagAsync(
            ownerClient,
            wishlistId,
            wishId,
            cancellationToken);

        // Act
        using var response = await UpdateWishQuantityAsync(
            ownerClient,
            wishlistId,
            wishId,
            1,
            wishEntityTag,
            cancellationToken);
        var storedWish = await GetStoredWishAsync(
            factory,
            wishlistId,
            wishId,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(
            "WISH_QUANTITY_BELOW_RESERVED",
            error.GetProperty("errorCode").GetString());
        Assert.Equal(
            3,
            storedWish.Quantity);
    }

    [Fact]
    public async Task UpdateWishAsync_WhenReservationIsCreatedConcurrently_PreservesQuantityInvariant()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var firstMemberId = Guid.CreateVersion7();
        var secondMemberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (firstMemberId, "first@example.test", "First participant"),
                (secondMemberId, "second@example.test", "Second participant")
            ],
            wishlistId,
            wishId,
            2,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var firstClient = CreateAuthorizedClient(
            factory,
            firstMemberId);
        using var secondClient = CreateAuthorizedClient(
            factory,
            secondMemberId);
        var firstCsrfToken = await GetCsrfTokenAsync(
            firstClient,
            cancellationToken);
        var secondCsrfToken = await GetCsrfTokenAsync(
            secondClient,
            cancellationToken);
        using var firstJoin = await JoinAsync(
            firstClient,
            shareLink.Id,
            shareLink.Secret,
            firstCsrfToken,
            cancellationToken);
        using var secondJoin = await JoinAsync(
            secondClient,
            shareLink.Id,
            shareLink.Secret,
            secondCsrfToken,
            cancellationToken);
        using var firstReservation = await UpsertAsync(
            firstClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            firstCsrfToken,
            1,
            null,
            cancellationToken);
        var wishEntityTag = await GetWishEntityTagAsync(
            ownerClient,
            wishlistId,
            wishId,
            cancellationToken);

        // Act
        var operations = new[]
        {
            UpdateWishQuantityAsync(
                ownerClient,
                wishlistId,
                wishId,
                1,
                wishEntityTag,
                cancellationToken),
            UpsertAsync(
                secondClient,
                shareLink.Id,
                wishId,
                shareLink.Secret,
                secondCsrfToken,
                1,
                null,
                cancellationToken)
        };
        var responses = await Task.WhenAll(operations);
        var storedWish = await GetStoredWishAsync(
            factory,
            wishlistId,
            wishId,
            cancellationToken);
        var storedReservations = await GetStoredReservationsAsync(
            factory,
            cancellationToken);
        try
        {
            // Assert
            Assert.Equal(
                [
                    HttpStatusCode.Created,
                    HttpStatusCode.Conflict
                ],
                responses
                    .Select(response => response.StatusCode)
                    .Order());
            Assert.True(storedReservations.Sum(item => item.Quantity) <= storedWish.Quantity);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task PutAsync_WhenCommitAcknowledgementIsLost_ReturnsSingleCommittedReservation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateFactoryAsync(
            cancellationToken,
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (memberId, "participant@example.test", "Participant")
            ],
            wishlistId,
            wishId,
            2,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var participantClient = CreateAuthorizedClient(
            factory,
            memberId);
        var csrfToken = await GetCsrfTokenAsync(
            participantClient,
            cancellationToken);
        using var join = await JoinAsync(
            participantClient,
            shareLink.Id,
            shareLink.Secret,
            csrfToken,
            cancellationToken);
        interceptor.Arm();

        // Act
        using var response = await UpsertAsync(
            participantClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            csrfToken,
            1,
            null,
            cancellationToken);
        var reservations = await GetStoredReservationsAsync(
            factory,
            cancellationToken);
        var histories = await GetStoredHistoriesAsync(
            factory,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        var reservation = Assert.Single(reservations);
        Assert.Equal(
            1,
            reservation.Quantity);
        var history = Assert.Single(histories);
        Assert.Equal(
            reservation.Id,
            history.Id);
    }

    [Fact]
    public async Task JoinAsync_WhenGuestCommitAcknowledgementIsLost_ReturnsUsableOriginalSession()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateFactoryAsync(
            cancellationToken,
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [],
            wishlistId,
            Guid.CreateVersion7(),
            1,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var guestClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        var csrfToken = await GetCsrfTokenAsync(
            guestClient,
            cancellationToken);
        interceptor.Arm();

        // Act
        using var response = await JoinGuestAsync(
            guestClient,
            shareLink.Id,
            shareLink.Secret,
            csrfToken,
            cancellationToken);
        using var currentParticipant = await GetCurrentParticipantAsync(
            guestClient,
            shareLink.Id,
            shareLink.Secret,
            cancellationToken);
        var participants = await GetStoredParticipantsAsync(
            factory,
            cancellationToken);
        var guestSessions = await GetStoredGuestSessionsAsync(
            factory,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            currentParticipant.StatusCode);
        Assert.Single(participants);
        Assert.Single(guestSessions);
    }

    [Fact]
    public async Task JoinAsync_WhenGuestAttachmentCommitAcknowledgementIsLost_ReturnsCommittedMemberParticipant()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateFactoryAsync(
            cancellationToken,
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (memberId, "participant@example.test", "Participant")
            ],
            wishlistId,
            Guid.CreateVersion7(),
            1,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var participantClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        var guestCsrfToken = await GetCsrfTokenAsync(
            participantClient,
            cancellationToken);
        using var guestJoin = await JoinGuestAsync(
            participantClient,
            shareLink.Id,
            shareLink.Secret,
            guestCsrfToken,
            cancellationToken);
        AuthorizeClient(
            factory,
            participantClient,
            memberId);
        var memberCsrfToken = await GetCsrfTokenAsync(
            participantClient,
            cancellationToken);
        interceptor.Arm();

        // Act
        using var response = await JoinAsync(
            participantClient,
            shareLink.Id,
            shareLink.Secret,
            memberCsrfToken,
            cancellationToken);
        var participants = await GetStoredParticipantsAsync(
            factory,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var participant = Assert.Single(participants);
        Assert.Equal(
            memberId,
            participant.MemberId);
        Assert.Null(participant.GuestSessionId);
    }

    [Fact]
    public async Task DeleteAsync_WhenReservationExists_DeletesCurrentReservationAndFreesCapacity()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var firstMemberId = Guid.CreateVersion7();
        var secondMemberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (firstMemberId, "first@example.test", "First participant"),
                (secondMemberId, "second@example.test", "Second participant")
            ],
            wishlistId,
            wishId,
            2,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var firstClient = CreateAuthorizedClient(
            factory,
            firstMemberId);
        using var secondClient = CreateAuthorizedClient(
            factory,
            secondMemberId);
        var firstCsrfToken = await GetCsrfTokenAsync(
            firstClient,
            cancellationToken);
        var secondCsrfToken = await GetCsrfTokenAsync(
            secondClient,
            cancellationToken);
        using var firstJoin = await JoinAsync(
            firstClient,
            shareLink.Id,
            shareLink.Secret,
            firstCsrfToken,
            cancellationToken);
        using var secondJoin = await JoinAsync(
            secondClient,
            shareLink.Id,
            shareLink.Secret,
            secondCsrfToken,
            cancellationToken);
        using var creation = await UpsertAsync(
            firstClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            firstCsrfToken,
            1,
            null,
            cancellationToken);
        var firstEntityTag = creation.Headers.ETag?.Tag;
        using var replacement = await UpsertAsync(
            firstClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            firstCsrfToken,
            2,
            firstEntityTag,
            cancellationToken);
        var secondEntityTag = replacement.Headers.ETag?.Tag;

        // Act
        using var missingEntityTag = await CancelAsync(
            firstClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            firstCsrfToken,
            entityTag: null,
            cancellationToken);
        using var staleCancellation = await CancelAsync(
            firstClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            firstCsrfToken,
            firstEntityTag,
            cancellationToken);
        using var cancellation = await CancelAsync(
            firstClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            firstCsrfToken,
            secondEntityTag,
            cancellationToken);
        using var repeatedCancellation = await CancelAsync(
            firstClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            firstCsrfToken,
            secondEntityTag,
            cancellationToken);
        using var missingReservation = await GetCurrentAsync(
            firstClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            cancellationToken);
        using var secondReservation = await UpsertAsync(
            secondClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            secondCsrfToken,
            2,
            null,
            cancellationToken);
        using var sharedWishlist = await GetSharedWishlistAsync(
            secondClient,
            shareLink.Id,
            shareLink.Secret,
            availableOnly: false,
            cancellationToken);
        var storedReservations = await GetStoredReservationsAsync(
            factory,
            cancellationToken);
        using var firstHistoryResponse = await GetHistoryAsync(
            firstClient,
            cancellationToken);
        using var secondHistoryResponse = await GetHistoryAsync(
            secondClient,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            firstJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            secondJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            creation.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            replacement.StatusCode);
        Assert.NotEqual(
            firstEntityTag,
            secondEntityTag);
        Assert.Equal(
            HttpStatusCode.PreconditionRequired,
            missingEntityTag.StatusCode);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            staleCancellation.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            cancellation.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            repeatedCancellation.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            missingReservation.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            secondReservation.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            sharedWishlist.StatusCode);
        var sharedBody = await sharedWishlist.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var sharedWish = Assert.Single(sharedBody.GetProperty("wishes").EnumerateArray());
        Assert.Equal(
            2,
            sharedWish.GetProperty("reservedQuantity").GetInt32());
        Assert.Equal(
            0,
            sharedWish.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(
            2,
            sharedWish.GetProperty("currentParticipantReservedQuantity").GetInt32());
        var storedReservation = Assert.Single(storedReservations);
        Assert.Equal(
            2,
            storedReservation.Quantity);
        var firstHistory = await GetSingleHistoryItemAsync(
            firstHistoryResponse,
            cancellationToken);
        Assert.Equal(
            2,
            firstHistory.GetProperty("quantity").GetInt32());
        Assert.Equal(
            "cancelled",
            firstHistory.GetProperty("status").GetString());
        Assert.Equal(
            JsonValueKind.String,
            firstHistory.GetProperty("endedAt").ValueKind);
        var secondHistory = await GetSingleHistoryItemAsync(
            secondHistoryResponse,
            cancellationToken);
        Assert.Equal(
            "active",
            secondHistory.GetProperty("status").GetString());
    }

    [Fact]
    public async Task JoinAsync_WhenGuestWithActiveReservationBecomesMember_AdoptsReservationHistory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (memberId, "participant@example.test", "Participant")
            ],
            wishlistId,
            wishId,
            3,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var guestClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        var guestCsrfToken = await GetCsrfTokenAsync(
            guestClient,
            cancellationToken);
        using var guestJoin = await JoinGuestAsync(
            guestClient,
            shareLink.Id,
            shareLink.Secret,
            guestCsrfToken,
            cancellationToken);
        using var guestReservation = await UpsertAsync(
            guestClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            guestCsrfToken,
            2,
            null,
            cancellationToken);
        var guestReservationBody = await guestReservation.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        AuthorizeClient(
            factory,
            guestClient,
            memberId);
        var memberCsrfToken = await GetCsrfTokenAsync(
            guestClient,
            cancellationToken);

        // Act
        using var memberJoin = await JoinAsync(
            guestClient,
            shareLink.Id,
            shareLink.Secret,
            memberCsrfToken,
            cancellationToken);
        using var historyResponse = await GetHistoryAsync(
            guestClient,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            guestJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            guestReservation.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            memberJoin.StatusCode);
        var history = await GetSingleHistoryItemAsync(
            historyResponse,
            cancellationToken);
        Assert.Equal(
            guestReservationBody.GetProperty("id").GetGuid(),
            history.GetProperty("id").GetGuid());
        Assert.Equal(
            2,
            history.GetProperty("quantity").GetInt32());
        Assert.Equal(
            "active",
            history.GetProperty("status").GetString());
    }

    [Fact]
    public async Task JoinAsync_WhenGuestCancelledBeforeLogin_DoesNotCreateReservationHistory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (memberId, "participant@example.test", "Participant")
            ],
            wishlistId,
            wishId,
            3,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var guestClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        var guestCsrfToken = await GetCsrfTokenAsync(
            guestClient,
            cancellationToken);
        using var guestJoin = await JoinGuestAsync(
            guestClient,
            shareLink.Id,
            shareLink.Secret,
            guestCsrfToken,
            cancellationToken);
        using var guestReservation = await UpsertAsync(
            guestClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            guestCsrfToken,
            1,
            null,
            cancellationToken);
        using var guestCancellation = await CancelAsync(
            guestClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            guestCsrfToken,
            guestReservation.Headers.ETag?.Tag,
            cancellationToken);
        AuthorizeClient(
            factory,
            guestClient,
            memberId);
        var memberCsrfToken = await GetCsrfTokenAsync(
            guestClient,
            cancellationToken);

        // Act
        using var memberJoin = await JoinAsync(
            guestClient,
            shareLink.Id,
            shareLink.Secret,
            memberCsrfToken,
            cancellationToken);
        using var historyResponse = await GetHistoryAsync(
            guestClient,
            cancellationToken);
        var historyBody = await historyResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            guestJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            guestReservation.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            guestCancellation.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            memberJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            historyResponse.StatusCode);
        Assert.Empty(historyBody.GetProperty("items").EnumerateArray());
        Assert.Equal(
            0,
            historyBody.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task JoinAsync_WhenGuestBecomesExistingMember_MergesReservationsWithoutChangingAggregate()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(cancellationToken);
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        await SeedAsync(
            factory,
            ownerId,
            [
                (memberId, "participant@example.test", "Participant")
            ],
            wishlistId,
            wishId,
            5,
            cancellationToken);
        using var ownerClient = CreateAuthorizedClient(
            factory,
            ownerId);
        var shareLink = await CreateShareLinkAsync(
            ownerClient,
            wishlistId,
            cancellationToken);
        using var guestClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        var guestCsrfToken = await GetCsrfTokenAsync(
            guestClient,
            cancellationToken);
        using var guestJoin = await JoinGuestAsync(
            guestClient,
            shareLink.Id,
            shareLink.Secret,
            guestCsrfToken,
            cancellationToken);
        using var guestReservation = await UpsertAsync(
            guestClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            guestCsrfToken,
            2,
            null,
            cancellationToken);
        using var memberClient = CreateAuthorizedClient(
            factory,
            memberId);
        var memberCsrfToken = await GetCsrfTokenAsync(
            memberClient,
            cancellationToken);
        using var memberJoin = await JoinAsync(
            memberClient,
            shareLink.Id,
            shareLink.Secret,
            memberCsrfToken,
            cancellationToken);
        using var memberReservation = await UpsertAsync(
            memberClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            memberCsrfToken,
            1,
            null,
            cancellationToken);
        var memberReservationBody = await memberReservation.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        AuthorizeClient(
            factory,
            guestClient,
            memberId);
        var memberOnGuestClientCsrfToken = await GetCsrfTokenAsync(
            guestClient,
            cancellationToken);

        // Act
        using var mergedJoin = await JoinAsync(
            guestClient,
            shareLink.Id,
            shareLink.Secret,
            memberOnGuestClientCsrfToken,
            cancellationToken);
        using var currentReservation = await GetCurrentAsync(
            guestClient,
            shareLink.Id,
            wishId,
            shareLink.Secret,
            cancellationToken);
        var currentBody = await currentReservation.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var storedReservations = await GetStoredReservationsAsync(
            factory,
            cancellationToken);
        var storedParticipants = await GetStoredParticipantsAsync(
            factory,
            cancellationToken);
        using var historyResponse = await GetHistoryAsync(
            guestClient,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            guestJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            guestReservation.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            memberJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            memberReservation.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            mergedJoin.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            currentReservation.StatusCode);
        Assert.Equal(
            3,
            currentBody.GetProperty("quantity").GetInt32());
        var storedReservation = Assert.Single(storedReservations);
        Assert.Equal(
            3,
            storedReservation.Quantity);
        var storedParticipant = Assert.Single(storedParticipants);
        Assert.Equal(
            memberId,
            storedParticipant.MemberId);
        var history = await GetSingleHistoryItemAsync(
            historyResponse,
            cancellationToken);
        Assert.Equal(
            memberReservationBody.GetProperty("id").GetGuid(),
            history.GetProperty("id").GetGuid());
        Assert.Equal(
            3,
            history.GetProperty("quantity").GetInt32());
        Assert.Equal(
            "active",
            history.GetProperty("status").GetString());
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: configureServices);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await fixture.ResetDatabaseAsync(cancellationToken);

        return factory;
    }

    private static async Task SeedAsync(
        PostgreSqlApiFactory factory,
        Guid ownerId,
        IReadOnlyCollection<(Guid Id, string Email, string DisplayName)> participants,
        Guid wishlistId,
        Guid wishId,
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.Users.Add(CreateMember(
            ownerId,
            "owner@example.test",
            "Owner"));
        context.Users.AddRange(participants.Select(participant => CreateMember(
            participant.Id,
            participant.Email,
            participant.DisplayName)));
        context.Wishlists.Add(new Wishlist(
            wishlistId,
            ownerId,
            "Birthday",
            "BIRTHDAY",
            WishlistOccasion.Birthday,
            null,
            null));
        context.Wishes.Add(new Wish(
            wishId,
            wishlistId,
            "Gift",
            null,
            null,
            null,
            1,
            quantity));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static MonKadoUser CreateMember(
        Guid id,
        string email,
        string displayName)
    {
        return new MonKadoUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = displayName,
            SecurityStamp = Guid.CreateVersion7().ToString()
        };
    }

    private static HttpClient CreateAuthorizedClient(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        AuthorizeClient(
            factory,
            client,
            memberId);

        return client;
    }

    private static void AuthorizeClient(
        PostgreSqlApiFactory factory,
        HttpClient client,
        Guid memberId)
    {
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);
    }

    private static async Task<(Guid Id, string Secret, string? EntityTag)> CreateShareLinkAsync(
        HttpClient ownerClient,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        using var response = await ownerClient.PostAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var shareUrl = body.GetProperty("shareUrl").GetString()
            ?? throw new InvalidOperationException("The share URL is missing.");

        return (
            body.GetProperty("id").GetGuid(),
            shareUrl[(shareUrl.LastIndexOf('.') + 1)..],
            response.Headers.ETag?.Tag);
    }

    private static async Task<HttpResponseMessage> RotateShareLinkAsync(
        HttpClient ownerClient,
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

        return await ownerClient.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> RevokeShareLinkAsync(
        HttpClient ownerClient,
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

        return await ownerClient.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<string?> GetWishEntityTagAsync(
        HttpClient ownerClient,
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        using var response = await ownerClient.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return response.Headers.ETag?.Tag;
    }

    private static async Task<HttpResponseMessage> DeleteWishAsync(
        HttpClient ownerClient,
        Guid wishlistId,
        Guid wishId,
        string? entityTag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}");
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        return await ownerClient.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> UpdateWishQuantityAsync(
        HttpClient ownerClient,
        Guid wishlistId,
        Guid wishId,
        int quantity,
        string? entityTag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}")
        {
            Content = JsonContent.Create(new
            {
                name = "Gift",
                quantity
            })
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        return await ownerClient.SendAsync(
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
            cancellationToken) ?? throw new InvalidOperationException("The CSRF token response is empty.");

        return payload.Token;
    }

    private static async Task<HttpResponseMessage> JoinAsync(
        HttpClient client,
        Guid shareLinkId,
        string secret,
        string csrfToken,
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
            csrfToken);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> JoinGuestAsync(
        HttpClient client,
        Guid shareLinkId,
        string secret,
        string csrfToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/shared-wishlists/{shareLinkId}/participants")
        {
            Content = JsonContent.Create(new
            {
                displayName = "Guest"
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

    private static async Task<HttpResponseMessage> UpsertAsync(
        HttpClient client,
        Guid shareLinkId,
        Guid wishId,
        string secret,
        string csrfToken,
        int quantity,
        string? entityTag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/reservations/current")
        {
            Content = JsonContent.Create(new
            {
                quantity
            })
        };
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            secret);
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        if (entityTag is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);
        }

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> GetCurrentAsync(
        HttpClient client,
        Guid shareLinkId,
        Guid wishId,
        string secret,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/reservations/current");
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

    private static Task<HttpResponseMessage> GetHistoryAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        return client.GetAsync(
            "/api/v1/members/current/reservations",
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> CancelAsync(
        HttpClient client,
        Guid shareLinkId,
        Guid wishId,
        string secret,
        string csrfToken,
        string? entityTag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/reservations/current");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            secret);
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        if (entityTag is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);
        }

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> GetSharedWishlistAsync(
        HttpClient client,
        Guid shareLinkId,
        string secret,
        bool availableOnly,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}?availableOnly={availableOnly}");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            secret);

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<IReadOnlyCollection<GiftReservation>> GetStoredReservationsAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.GiftReservations
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<Wish> GetStoredWishAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Wishes
            .AsNoTracking()
            .SingleAsync(
                wish => wish.WishlistId == wishlistId && wish.Id == wishId,
                cancellationToken);
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

    private static async Task<IReadOnlyCollection<GuestSession>> GetStoredGuestSessionsAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.GuestSessions
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<IReadOnlyCollection<GiftReservationHistory>> GetStoredHistoriesAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.GiftReservationHistories
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
    }

    private static async Task AddHistoriesAsync(
        PostgreSqlApiFactory factory,
        IReadOnlyCollection<GiftReservationHistory> histories,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.GiftReservationHistories.AddRange(histories);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task DeleteMemberAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<JsonElement> GetSingleHistoryItemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        return Assert.Single(body.GetProperty("items").EnumerateArray());
    }
}
