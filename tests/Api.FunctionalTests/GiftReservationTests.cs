using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class GiftReservationTests
{
    private const string ShareSecret = "public-secret";

    [Fact]
    public async Task UpsertCurrentReservationAsync_WhenReservationIsNew_ReturnsCreatedContract()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            memberId);

        // Act
        using var response = await SendPutAsync(
            client,
            shareLinkId,
            wishId,
            2,
            entityTag: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        Assert.Equal(
            $"http://localhost/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/reservations/current",
            response.Headers.Location?.AbsoluteUri);
        Assert.Equal(
            "noindex, nofollow, noarchive",
            response.Headers.GetValues("X-Robots-Tag").Single());
        var mutation = Assert.Single(factory.GiftReservationService.Mutations);
        Assert.Equal(
            memberId,
            mutation.MemberId);
        Assert.Equal(
            2,
            mutation.Quantity);
        Assert.Null(mutation.ExpectedVersion);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-reservation response is empty.");
        AssertReservationContract(
            document.RootElement,
            mutation.ReservationId,
            wishId,
            2);
    }

    [Fact]
    public async Task UpsertCurrentReservationAsync_WhenReservationExists_ReturnsOkWithVersion()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.GiftReservationService.IsCreated = false;
        factory.GiftReservationService.Reservation = new GiftReservationDetails
        {
            Id = Guid.CreateVersion7(),
            WishId = Guid.CreateVersion7(),
            Quantity = 3,
            CreatedAt = new DateTime(
                2026,
                8,
                30,
                10,
                0,
                0,
                DateTimeKind.Utc),
            UpdatedAt = new DateTime(
                2026,
                8,
                30,
                11,
                0,
                0,
                DateTimeKind.Utc),
            Version = 43
        };
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await SendPutAsync(
            client,
            Guid.CreateVersion7(),
            factory.GiftReservationService.Reservation.WishId,
            3,
            "\"0000002a\"",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "\"0000002b\"",
            response.Headers.ETag?.Tag);
        Assert.Null(response.Headers.Location);
        var mutation = Assert.Single(factory.GiftReservationService.Mutations);
        Assert.Equal(
            42u,
            mutation.ExpectedVersion);
    }

    [Fact]
    public async Task GetCurrentReservationAsync_WhenReservationExists_ReturnsOnlyCurrentReservation()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Jenn");
        var reservation = new GiftReservationDetails
        {
            Id = Guid.CreateVersion7(),
            WishId = Guid.CreateVersion7(),
            Quantity = 2,
            CreatedAt = new DateTime(
                2026,
                8,
                30,
                10,
                0,
                0,
                DateTimeKind.Utc),
            Version = 42
        };
        factory.WishlistParticipantService.LookupResult = new WishlistParticipantLookupResult(
            WishlistParticipantLookupOutcome.Found,
            participant);
        factory.GiftReservationService.Reservation = reservation;
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        var shareLinkId = Guid.CreateVersion7();
        using var request = CreateSharedRequest(
            HttpMethod.Get,
            shareLinkId,
            reservation.WishId);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The gift-reservation response is empty.");
        AssertReservationContract(
            document.RootElement,
            reservation.Id,
            reservation.WishId,
            2);
        Assert.Equal(
            [(factory.WishlistShareService.SharedWishlist?.Id ?? Guid.Empty, reservation.WishId, participant.Id)],
            factory.GiftReservationService.Retrievals);
    }

    [Theory]
    [InlineData(typeof(GiftReservationNotFoundException), HttpStatusCode.NotFound, ErrorCodes.GiftReservationNotFound)]
    [InlineData(typeof(GiftReservationQuantityUnavailableException), HttpStatusCode.Conflict, ErrorCodes.GiftReservationQuantityUnavailable)]
    [InlineData(typeof(GiftReservationVersionConflictException), HttpStatusCode.PreconditionFailed, ErrorCodes.GiftReservationVersionConflict)]
    [InlineData(typeof(PreconditionRequiredException), HttpStatusCode.PreconditionRequired, ErrorCodes.RequestPreconditionRequired)]
    [InlineData(typeof(DependencyUnavailableException), HttpStatusCode.ServiceUnavailable, ErrorCodes.TechnicalDependencyUnavailable)]
    public async Task UpsertCurrentReservationAsync_WhenServiceRejectsMutation_ReturnsStructuredError(
        Type exceptionType,
        HttpStatusCode expectedStatusCode,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.GiftReservationService.Exception = CreateException(exceptionType);
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await SendPutAsync(
            client,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            2,
            entityTag: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatusCode,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            expectedErrorCode,
            error.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(101)]
    public async Task UpsertCurrentReservationAsync_WhenQuantityIsInvalid_ReturnsBadRequest(int? quantity)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await SendPutAsync(
            client,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            quantity,
            entityTag: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains(
            error.ValidationErrors ?? [],
            validation => validation.PropertyName == "quantity");
        Assert.Empty(factory.GiftReservationService.Mutations);
    }

    [Fact]
    public async Task UpsertCurrentReservationAsync_WhenAntiforgeryTokenIsMissing_ReturnsBadRequest()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateSharedRequest(
            HttpMethod.Put,
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        request.Content = JsonContent.Create(new
        {
            quantity = 1
        });

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.GiftReservationService.Mutations);
    }

    [Fact]
    public async Task UpsertCurrentReservationAsync_WhenRateLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var csrfToken = await GetCsrfTokenAsync(
            client,
            TestContext.Current.CancellationToken);
        HttpResponseMessage? response = null;

        try
        {
            // Act
            for (var requestNumber = 0;
                requestNumber <= AuthenticationRateLimitingExtensions.SharedWishlistReservationPermitLimit;
                requestNumber++)
            {
                response?.Dispose();
                response = await SendPutWithCsrfAsync(
                    client,
                    shareLinkId,
                    wishId,
                    1,
                    entityTag: null,
                    csrfToken,
                    TestContext.Current.CancellationToken);
            }

            // Assert
            Assert.NotNull(response);
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
                TestContext.Current.CancellationToken);
            Assert.NotNull(error);
            Assert.Equal(
                ErrorCodes.RequestRateLimitExceeded,
                error.ErrorCode);
            Assert.Equal(
                AuthenticationRateLimitingExtensions.SharedWishlistReservationPermitLimit,
                factory.GiftReservationService.Mutations.Count);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> SendPutAsync(
        HttpClient client,
        Guid shareLinkId,
        Guid wishId,
        int? quantity,
        string? entityTag,
        CancellationToken cancellationToken)
    {
        var csrfToken = await GetCsrfTokenAsync(
            client,
            cancellationToken);

        return await SendPutWithCsrfAsync(
            client,
            shareLinkId,
            wishId,
            quantity,
            entityTag,
            csrfToken,
            cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendPutWithCsrfAsync(
        HttpClient client,
        Guid shareLinkId,
        Guid wishId,
        int? quantity,
        string? entityTag,
        string csrfToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateSharedRequest(
            HttpMethod.Put,
            shareLinkId,
            wishId);
        request.Content = JsonContent.Create(new
        {
            quantity
        });
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

    private static HttpRequestMessage CreateSharedRequest(
        HttpMethod method,
        Guid shareLinkId,
        Guid wishId)
    {
        var request = new HttpRequestMessage(
            method,
            $"/api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/reservations/current");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            ShareSecret);

        return request;
    }

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var response = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/security/csrf-token",
            cancellationToken);

        return response?.Token
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
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

    private static void AssertReservationContract(
        JsonElement reservation,
        Guid reservationId,
        Guid wishId,
        int quantity)
    {
        Assert.Equal(
            [
                "id",
                "wishId",
                "quantity",
                "createdAt",
                "updatedAt"
            ],
            reservation
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            reservationId,
            reservation.GetProperty("id").GetGuid());
        Assert.Equal(
            wishId,
            reservation.GetProperty("wishId").GetGuid());
        Assert.Equal(
            quantity,
            reservation.GetProperty("quantity").GetInt32());
    }

    private static Exception CreateException(Type exceptionType)
    {
        if (exceptionType == typeof(GiftReservationNotFoundException))
            return new GiftReservationNotFoundException();

        if (exceptionType == typeof(GiftReservationQuantityUnavailableException))
            return new GiftReservationQuantityUnavailableException();

        if (exceptionType == typeof(GiftReservationVersionConflictException))
            return new GiftReservationVersionConflictException();

        if (exceptionType == typeof(PreconditionRequiredException))
            return new PreconditionRequiredException();

        return new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
    }
}
