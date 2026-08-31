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
            rotation.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            revocation.StatusCode);
        Assert.Single(reservationsAfterRevocation);
        Assert.Equal(
            HttpStatusCode.NoContent,
            wishDeletion.StatusCode);
        Assert.Empty(reservationsAfterWishDeletion);
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
}
