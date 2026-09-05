using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistShareLinkTests
{
    [Fact]
    public async Task CreateAsync_WhenBearerIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/share-link",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.WishlistShareService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenWishlistIsOwned_ReturnsCopyableShareLink()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.PostAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        var creation = Assert.Single(factory.WishlistShareService.Creations);
        Assert.Equal(
            ownerId,
            creation.OwnerId);
        Assert.Equal(
            wishlistId,
            creation.WishlistId);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The share-link response is empty.");
        Assert.Equal(
            [
                "id",
                "shareUrl",
                "createdAt",
                "updatedAt"
            ],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            $"http://localhost:5173/#/shared-wishlists/{creation.Id:N}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            document.RootElement.GetProperty("shareUrl").GetString());
        Assert.Equal(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task RotateAsync_WhenVersionMatches_ReturnsNewSecretAndEntityTag()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        await factory.WishlistShareService.CreateAsync(
            shareLinkId,
            ownerId,
            wishlistId,
            TestContext.Current.CancellationToken);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/share-link");
        request.Headers.TryAddWithoutValidation(
            HeaderNames.IfMatch,
            "\"0000002a\"");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "\"0000002b\"",
            response.Headers.ETag?.Tag);
        Assert.Equal(
            [(ownerId, wishlistId, 42u)],
            factory.WishlistShareService.Rotations);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(
            ".BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            content,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOwnerLinkAsync_WhenShareLinkExists_ReturnsSameCopyableUrl()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        await factory.WishlistShareService.CreateAsync(
            shareLinkId,
            ownerId,
            wishlistId,
            TestContext.Current.CancellationToken);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        Assert.Equal(
            [(ownerId, wishlistId)],
            factory.WishlistShareService.Retrievals);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The share-link response is empty.");
        Assert.Equal(
            shareLinkId,
            document.RootElement.GetProperty("id").GetGuid());
        Assert.Contains(
            ".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            document.RootElement.GetProperty("shareUrl").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOwnerLinkAsync_WhenShareLinkDoesNotExist_ReturnsStructuredNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            "WISHLIST_SHARE_LINK_NOT_FOUND",
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task CreateAsync_WhenWishlistIsNotOwned_ReturnsNonDisclosingNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        factory.WishlistService.Access = WishlistAccess.NotOwned;
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.PostAsync(
            $"/api/v1/wishlists/{wishlistId}/share-link",
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        Assert.Empty(factory.WishlistShareService.Creations);
    }

    [Fact]
    public async Task DeleteAsync_WhenVersionMatches_RevokesShareLink()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        await factory.WishlistShareService.CreateAsync(
            Guid.CreateVersion7(),
            ownerId,
            wishlistId,
            TestContext.Current.CancellationToken);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlistId}/share-link");
        request.Headers.TryAddWithoutValidation(
            HeaderNames.IfMatch,
            "\"0000002a\"");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.Equal(
            [(ownerId, wishlistId, 42u)],
            factory.WishlistShareService.Deletions);
        Assert.Empty(factory.WishlistShareService.ShareLinks);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task MutateAsync_WhenIfMatchIsMissing_ReturnsPreconditionRequired(string method)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/v1/wishlists/{wishlistId}/share-link");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            (HttpStatusCode)StatusCodes.Status428PreconditionRequired,
            response.StatusCode);
        Assert.Empty(factory.WishlistShareService.Rotations);
        Assert.Empty(factory.WishlistShareService.Deletions);
    }

    [Fact]
    public async Task GetAsync_WhenShareTokenIsValid_ReturnsOnlyPublicContent()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var shareLinkId = Guid.CreateVersion7();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            "public-secret");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "noindex, nofollow, noarchive",
            response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal(
            [(shareLinkId, "public-secret")],
            factory.WishlistShareService.PublicRetrievals);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The shared-wishlist response is empty.");
        Assert.Equal(
            [
                "id",
                "ownerDisplayName",
                "name",
                "occasion",
                "eventDate",
                "message",
                "wishes",
                "currentParticipant"
            ],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name));
        var wish = document.RootElement.GetProperty("wishes")[0];
        Assert.Equal(
            [
                "id",
                "name",
                "url",
                "price",
                "quantity",
                "reservedQuantity",
                "availableQuantity",
                "currentParticipantReservedQuantity"
            ],
            wish
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.False(wish.TryGetProperty(
            "note",
            out _));
        Assert.Equal(
            1,
            wish.GetProperty("quantity").GetInt32());
        Assert.Equal(
            0,
            wish.GetProperty("reservedQuantity").GetInt32());
        Assert.Equal(
            1,
            wish.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(
            JsonValueKind.Null,
            wish.GetProperty("currentParticipantReservedQuantity").ValueKind);
        Assert.Equal(
            "Jenn",
            document.RootElement.GetProperty("ownerDisplayName").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("currentParticipant").ValueKind);
    }

    [Fact]
    public async Task GetAsync_WhenMemberJoinedOverreservedWishlist_ClampsAvailabilityAndReturnsCurrentQuantity()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Participant");
        var wishlist = new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            [
                new SharedWishDetails(
                    wishId,
                    "Gift",
                    null,
                    null,
                    1,
                    3)
            ]);
        factory.WishlistShareService.SharedWishlist = wishlist;
        factory.WishlistParticipantService.LookupResult = new WishlistParticipantLookupResult(
            WishlistParticipantLookupOutcome.Found,
            participant);
        factory.GiftReservationService.Quantities[wishId] = 2;
        using var client = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var response = await SendPublicRequestAsync(
            client,
            shareLinkId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The shared-wishlist response is empty.");
        var wish = Assert.Single(document.RootElement.GetProperty("wishes").EnumerateArray());
        Assert.Equal(
            1,
            wish.GetProperty("quantity").GetInt32());
        Assert.Equal(
            3,
            wish.GetProperty("reservedQuantity").GetInt32());
        Assert.Equal(
            0,
            wish.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(
            2,
            wish.GetProperty("currentParticipantReservedQuantity").GetInt32());
        Assert.Equal(
            participant.Id,
            document.RootElement
                .GetProperty("currentParticipant")
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            [(wishlist.Id, participant.Id)],
            factory.GiftReservationService.QuantityRetrievals);
    }

    [Fact]
    public async Task GetAsync_WhenAvailableOnlyIsTrue_FiltersFullyReservedWishes()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var shareLinkId = Guid.CreateVersion7();
        var availableWishId = Guid.CreateVersion7();
        var wishlist = new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            [
                new SharedWishDetails(
                    availableWishId,
                    "Available gift",
                    null,
                    null,
                    2,
                    1),
                new SharedWishDetails(
                    Guid.CreateVersion7(),
                    "Reserved gift",
                    null,
                    null,
                    1,
                    1),
                new SharedWishDetails(
                    Guid.CreateVersion7(),
                    "Overreserved gift",
                    null,
                    null,
                    1,
                    2)
            ]);
        factory.WishlistShareService.SharedWishlist = wishlist;
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}?availableOnly=true");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            "public-secret");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The shared-wishlist response is empty.");
        var wish = Assert.Single(document.RootElement.GetProperty("wishes").EnumerateArray());
        Assert.Equal(
            availableWishId,
            wish.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetAsync_WhenAvailableOnlyIsInvalid_ReturnsStructuredBadRequest()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/shared-wishlists/{Guid.CreateVersion7()}?availableOnly=invalid",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            error.StatusCode);
        Assert.NotNull(error.ValidationErrors);
    }

    [Fact]
    public async Task GetWishAsync_WhenShareTokenIsValid_ReturnsDetailedPublicContent()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var lookup = factory.WishlistShareService.SharedWishLookupResult;
        var wishlistId = lookup.WishlistId
            ?? throw new InvalidOperationException("The fake shared wishlist is missing.");
        var wish = lookup.Wish
            ?? throw new InvalidOperationException("The fake shared wish is missing.");

        // Act
        using var response = await SendPublicWishRequestAsync(
            client,
            Guid.CreateVersion7(),
            wish.Id,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "noindex, nofollow, noarchive",
            response.Headers.GetValues("X-Robots-Tag").Single());
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The shared-wish response is empty.");
        Assert.Equal(
            [
                "id",
                "name",
                "note",
                "url",
                "price",
                "quantity",
                "reservedQuantity",
                "availableQuantity",
                "currentParticipantReservedQuantity"
            ],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            wish.Id,
            document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(
            wish.Note,
            document.RootElement.GetProperty("note").GetString());
        Assert.Equal(
            1,
            document.RootElement.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("currentParticipantReservedQuantity").ValueKind);
        var retrieval = Assert.Single(factory.WishlistShareService.PublicWishRetrievals);
        Assert.Equal(
            wish.Id,
            retrieval.WishId);
        Assert.Equal(
            "public-secret",
            retrieval.Secret);
        Assert.Equal(
            [(wishlistId, null, null)],
            factory.WishlistParticipantService.Retrievals);
        Assert.Empty(factory.GiftReservationService.Retrievals);
    }

    [Fact]
    public async Task GetWishAsync_WhenParticipantReservedOverreservedWish_ReturnsCurrentQuantity()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Participant");
        factory.WishlistShareService.SharedWishLookupResult = new SharedWishLookupResult(
            SharedWishLookupOutcome.Found,
            wishlistId,
            new SharedWishDetail
            {
                Id = wishId,
                Name = "Gift",
                Note = null,
                Url = null,
                Price = null,
                Quantity = 1,
                ReservedQuantity = 3,
                CurrentParticipantReservedQuantity = null
            });
        factory.WishlistParticipantService.LookupResult = new WishlistParticipantLookupResult(
            WishlistParticipantLookupOutcome.Found,
            participant);
        factory.GiftReservationService.Reservation = new GiftReservationDetails
        {
            Id = Guid.CreateVersion7(),
            WishId = wishId,
            Quantity = 2
        };
        using var client = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var response = await SendPublicWishRequestAsync(
            client,
            shareLinkId,
            wishId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The shared-wish response is empty.");
        Assert.Equal(
            0,
            document.RootElement.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("note").ValueKind);
        Assert.Equal(
            2,
            document.RootElement.GetProperty("currentParticipantReservedQuantity").GetInt32());
        Assert.Equal(
            [(wishlistId, memberId, null)],
            factory.WishlistParticipantService.Retrievals);
        Assert.Equal(
            [(wishlistId, wishId, participant.Id)],
            factory.GiftReservationService.Retrievals);
    }

    [Theory]
    [InlineData(SharedWishLookupOutcome.SharedWishlistNotFound, "SHARED_WISHLIST_NOT_FOUND")]
    [InlineData(SharedWishLookupOutcome.WishNotFound, "SHARED_WISH_NOT_FOUND")]
    public async Task GetWishAsync_WhenLookupFails_ReturnsExpectedStructuredNotFound(
        SharedWishLookupOutcome outcome,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var wishlistId = outcome is SharedWishLookupOutcome.WishNotFound
            ? Guid.CreateVersion7()
            : (Guid?)null;
        factory.WishlistShareService.SharedWishLookupResult = new SharedWishLookupResult(
            outcome,
            wishlistId,
            null);
        using var client = factory.CreateClient();

        // Act
        using var response = await SendPublicWishRequestAsync(
            client,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            expectedErrorCode,
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(factory.WishlistParticipantService.Retrievals);
        Assert.Empty(factory.GiftReservationService.Retrievals);
    }

    [Fact]
    public async Task GetWishAsync_WhenShareTokenIsMissing_ReturnsNonDisclosingNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/shared-wishlists/{Guid.CreateVersion7()}/wishes/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.SharedWishlistNotFound,
            error.ErrorCode);
        Assert.Empty(factory.WishlistShareService.PublicWishRetrievals);
    }

    [Fact]
    public async Task GetWishAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistShareService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = factory.CreateClient();

        // Act
        using var response = await SendPublicWishRequestAsync(
            client,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error.ErrorCode);
    }

    [Fact]
    public async Task GetWishAsync_WhenAuthenticatedMemberNoLongerExists_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        var wish = factory.WishlistShareService.SharedWishLookupResult.Wish
            ?? throw new InvalidOperationException("The fake shared wish is missing.");
        factory.WishlistParticipantService.LookupResult = new WishlistParticipantLookupResult(
            WishlistParticipantLookupOutcome.MemberNotFound,
            null);
        using var client = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var response = await SendPublicWishRequestAsync(
            client,
            Guid.CreateVersion7(),
            wish.Id,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            "ACCOUNT_AUTHENTICATION_SESSION_INVALID",
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(factory.GiftReservationService.Retrievals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetWishAsync_WhenIdentifierIsEmpty_ReturnsStructuredBadRequest(
        bool shareLinkIdIsEmpty)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var shareLinkId = shareLinkIdIsEmpty
            ? Guid.Empty
            : Guid.CreateVersion7();
        var wishId = shareLinkIdIsEmpty
            ? Guid.CreateVersion7()
            : Guid.Empty;

        // Act
        using var response = await SendPublicWishRequestAsync(
            client,
            shareLinkId,
            wishId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            error.StatusCode);
        var validationError = Assert.Single(error.ValidationErrors ?? []);
        Assert.Equal(
            shareLinkIdIsEmpty
                ? "shareLinkId"
                : "wishId",
            validationError.PropertyName);
    }

    [Fact]
    public async Task GetWishAsync_WhenPublicRateLimitIsExceeded_ReturnsStructuredTooManyRequests()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();

        // Act
        var response = await SendPublicWishRequestAsync(
            client,
            shareLinkId,
            wishId,
            TestContext.Current.CancellationToken);

        try
        {
            for (var requestNumber = 1;
                requestNumber <= AuthenticationRateLimitingExtensions.SharedWishlistPermitLimit;
                requestNumber++)
            {
                response.Dispose();
                response = await SendPublicWishRequestAsync(
                    client,
                    shareLinkId,
                    wishId,
                    TestContext.Current.CancellationToken);
            }

            // Assert
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
                TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The error response is empty.");
            Assert.Equal(
                "REQUEST_RATE_LIMIT_EXCEEDED",
                document.RootElement.GetProperty("errorCode").GetString());
        }
        finally
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task GetAsync_WhenShareTokenIsInvalid_ReturnsNonDisclosingNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistShareService.SharedWishlist = null;
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/shared-wishlists/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            "SHARED_WISHLIST_NOT_FOUND",
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public Task CreateAsync_WhenShareLinkAlreadyExists_ReturnsStructuredConflict()
    {
        return AssertMutationErrorAsync(
            new WishlistShareLinkAlreadyExistsException(),
            HttpMethod.Post,
            HttpStatusCode.Conflict,
            "WISHLIST_SHARE_LINK_ALREADY_EXISTS",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task RotateAsync_WhenVersionConflicts_ReturnsStructuredPreconditionFailure()
    {
        return AssertMutationErrorAsync(
            new WishlistShareLinkVersionConflictException(),
            HttpMethod.Put,
            HttpStatusCode.PreconditionFailed,
            "WISHLIST_SHARE_LINK_VERSION_CONFLICT",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetAsync_WhenPublicRateLimitIsExceeded_ReturnsStructuredTooManyRequests()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var shareLinkId = Guid.CreateVersion7();

        // Act
        var response = await SendPublicRequestAsync(
            client,
            shareLinkId,
            TestContext.Current.CancellationToken);

        try
        {
            for (var requestNumber = 1;
                requestNumber <= AuthenticationRateLimitingExtensions.SharedWishlistPermitLimit;
                requestNumber++)
            {
                response.Dispose();
                response = await SendPublicRequestAsync(
                    client,
                    shareLinkId,
                    TestContext.Current.CancellationToken);
            }

            // Assert
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
                TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The error response is empty.");
            Assert.Equal(
                "REQUEST_RATE_LIMIT_EXCEEDED",
                document.RootElement.GetProperty("errorCode").GetString());
        }
        finally
        {
            response.Dispose();
        }
    }

    private static async Task AssertMutationErrorAsync(
        Exception exception,
        HttpMethod method,
        HttpStatusCode expectedStatus,
        string expectedErrorCode,
        CancellationToken cancellationToken)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        factory.WishlistShareService.Exception = exception;
        using var request = new HttpRequestMessage(
            method,
            $"/api/v1/wishlists/{wishlistId}/share-link");

        if (method == HttpMethod.Put)
        {
            request.Headers.TryAddWithoutValidation(
                HeaderNames.IfMatch,
                "\"0000002a\"");
        }

        // Act
        using var response = await client.SendAsync(
            request,
            cancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            expectedErrorCode,
            document.RootElement.GetProperty("errorCode").GetString());
    }

    private static async Task<HttpResponseMessage> SendPublicRequestAsync(
        HttpClient client,
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            "public-secret");

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendPublicWishRequestAsync(
        HttpClient client,
        Guid shareLinkId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            "public-secret");

        return await client.SendAsync(
            request,
            cancellationToken);
    }

    private static HttpClient CreateAuthorizedClient(
        RegistrationApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
    }
}
