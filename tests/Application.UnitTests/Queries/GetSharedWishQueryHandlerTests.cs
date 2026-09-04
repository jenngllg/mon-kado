using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetSharedWishQueryHandlerTests
{
    private readonly GetSharedWishQueryHandler _handler;
    private readonly Mock<IWishlistParticipantService> _participantServiceMock;
    private readonly Mock<IGiftReservationService> _reservationServiceMock;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public GetSharedWishQueryHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _participantServiceMock = new Mock<IWishlistParticipantService>(MockBehavior.Strict);
        _reservationServiceMock = new Mock<IGiftReservationService>(MockBehavior.Strict);
        _handler = new GetSharedWishQueryHandler(
            _shareServiceMock.Object,
            _participantServiceMock.Object,
            _reservationServiceMock.Object,
            NullLogger<GetSharedWishQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenShareLinkIsInvalid_ThrowsSharedWishlistNotFoundException()
    {
        // Arrange
        var query = CreateQuery();
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareServiceMock
            .Setup(service => service.GetSharedWishAsync(
                query.ShareLinkId,
                query.Secret ?? string.Empty,
                query.WishId,
                cancellationToken))
            .ReturnsAsync(new SharedWishLookupResult(
                SharedWishLookupOutcome.SharedWishlistNotFound,
                null,
                null));

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        VerifyShareLookup(
            query,
            cancellationToken);
        _participantServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenWishIsUnavailable_ThrowsSharedWishNotFoundException()
    {
        // Arrange
        var query = CreateQuery();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareServiceMock
            .Setup(service => service.GetSharedWishAsync(
                query.ShareLinkId,
                query.Secret ?? string.Empty,
                query.WishId,
                cancellationToken))
            .ReturnsAsync(new SharedWishLookupResult(
                SharedWishLookupOutcome.WishNotFound,
                wishlistId,
                null));

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishNotFoundException>(action);
        VerifyShareLookup(
            query,
            cancellationToken);
        _participantServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WhenFoundLookupIsIncomplete_ThrowsSharedWishNotFoundException(
        bool wishlistIdIsMissing)
    {
        // Arrange
        var query = CreateQuery();
        var wishlistId = Guid.CreateVersion7();
        var wish = CreateWish(query.WishId);
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareServiceMock
            .Setup(service => service.GetSharedWishAsync(
                query.ShareLinkId,
                query.Secret ?? string.Empty,
                query.WishId,
                cancellationToken))
            .ReturnsAsync(new SharedWishLookupResult(
                SharedWishLookupOutcome.Found,
                wishlistIdIsMissing
                    ? null
                    : wishlistId,
                wishlistIdIsMissing
                    ? wish
                    : null));

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishNotFoundException>(action);
        VerifyShareLookup(
            query,
            cancellationToken);
        _participantServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenSecretIsNull_PassesEmptySecretToShareService()
    {
        // Arrange
        var query = new GetSharedWishQuery(
            Guid.CreateVersion7(),
            null,
            Guid.CreateVersion7(),
            null,
            null);
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareServiceMock
            .Setup(service => service.GetSharedWishAsync(
                query.ShareLinkId,
                string.Empty,
                query.WishId,
                cancellationToken))
            .ReturnsAsync(new SharedWishLookupResult(
                SharedWishLookupOutcome.SharedWishlistNotFound,
                null,
                null));

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        VerifyShareLookup(
            query,
            cancellationToken);
        _participantServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberNoLongerExists_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var query = CreateQuery(memberId);
        var wishlistId = Guid.CreateVersion7();
        var wish = CreateWish(query.WishId);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupFoundWish(
            query,
            wishlistId,
            wish,
            cancellationToken);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlistId,
                memberId,
                query.GuestToken,
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.MemberNotFound,
                null));

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        VerifyFoundLookup(
            query,
            wishlistId,
            memberId,
            cancellationToken);
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistParticipantLookupOutcome.MissingIdentity)]
    [InlineData(WishlistParticipantLookupOutcome.InvalidGuestSession)]
    [InlineData(WishlistParticipantLookupOutcome.NotJoined)]
    public async Task Handle_WhenCurrentParticipantIsUnavailable_ReturnsWishWithoutCurrentQuantity(
        WishlistParticipantLookupOutcome outcome)
    {
        // Arrange
        var query = CreateQuery();
        var wishlistId = Guid.CreateVersion7();
        var wish = CreateWish(query.WishId);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupFoundWish(
            query,
            wishlistId,
            wish,
            cancellationToken);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlistId,
                query.MemberId,
                query.GuestToken,
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                outcome,
                null));

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        AssertWish(
            wish,
            result,
            expectedCurrentQuantity: null);
        VerifyFoundLookup(
            query,
            wishlistId,
            query.MemberId,
            cancellationToken);
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenFoundParticipantIsMissing_ReturnsWishWithoutCurrentQuantity()
    {
        // Arrange
        var query = CreateQuery();
        var wishlistId = Guid.CreateVersion7();
        var wish = CreateWish(query.WishId);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupFoundWish(
            query,
            wishlistId,
            wish,
            cancellationToken);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlistId,
                query.MemberId,
                query.GuestToken,
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.Found,
                null));

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        AssertWish(
            wish,
            result,
            expectedCurrentQuantity: null);
        VerifyFoundLookup(
            query,
            wishlistId,
            query.MemberId,
            cancellationToken);
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public async Task Handle_WhenCurrentParticipantIsFound_ReturnsCurrentReservationQuantity(
        bool reservationExists,
        int expectedCurrentQuantity)
    {
        // Arrange
        var query = CreateQuery();
        var wishlistId = Guid.CreateVersion7();
        var wish = CreateWish(query.WishId);
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Participant");
        var reservation = reservationExists
            ? new GiftReservationDetails
            {
                Id = Guid.CreateVersion7(),
                WishId = wish.Id,
                Quantity = expectedCurrentQuantity
            }
            : null;
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupFoundWish(
            query,
            wishlistId,
            wish,
            cancellationToken);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlistId,
                query.MemberId,
                query.GuestToken,
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.Found,
                participant));
        _reservationServiceMock
            .Setup(service => service.GetAsync(
                wishlistId,
                wish.Id,
                participant.Id,
                cancellationToken))
            .ReturnsAsync(reservation);

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        AssertWish(
            wish,
            result,
            expectedCurrentQuantity);
        VerifyFoundLookup(
            query,
            wishlistId,
            query.MemberId,
            cancellationToken);
        _reservationServiceMock.Verify(
            service => service.GetAsync(
                wishlistId,
                wish.Id,
                participant.Id,
                cancellationToken),
            Times.Once);
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    private static GetSharedWishQuery CreateQuery(Guid? memberId = null)
    {
        return new GetSharedWishQuery(
            Guid.CreateVersion7(),
            "secret",
            Guid.CreateVersion7(),
            memberId,
            "guest");
    }

    private static SharedWishDetail CreateWish(Guid wishId)
    {
        return new SharedWishDetail(
            wishId,
            "Gift",
            "Public note",
            "https://example.test/gift",
            12.34m,
            3,
            1,
            null);
    }

    private void SetupFoundWish(
        GetSharedWishQuery query,
        Guid wishlistId,
        SharedWishDetail wish,
        CancellationToken cancellationToken)
    {
        _shareServiceMock
            .Setup(service => service.GetSharedWishAsync(
                query.ShareLinkId,
                query.Secret ?? string.Empty,
                query.WishId,
                cancellationToken))
            .ReturnsAsync(new SharedWishLookupResult(
                SharedWishLookupOutcome.Found,
                wishlistId,
                wish));
    }

    private void VerifyShareLookup(
        GetSharedWishQuery query,
        CancellationToken cancellationToken)
    {
        _shareServiceMock.Verify(
            service => service.GetSharedWishAsync(
                query.ShareLinkId,
                query.Secret ?? string.Empty,
                query.WishId,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
    }

    private void VerifyFoundLookup(
        GetSharedWishQuery query,
        Guid wishlistId,
        Guid? memberId,
        CancellationToken cancellationToken)
    {
        VerifyShareLookup(
            query,
            cancellationToken);
        _participantServiceMock.Verify(
            service => service.GetCurrentAsync(
                wishlistId,
                memberId,
                query.GuestToken,
                cancellationToken),
            Times.Once);
        _participantServiceMock.VerifyNoOtherCalls();
    }

    private static void AssertWish(
        SharedWishDetail expected,
        SharedWishDetail actual,
        int? expectedCurrentQuantity)
    {
        Assert.Equal(
            expected.Id,
            actual.Id);
        Assert.Equal(
            expected.Name,
            actual.Name);
        Assert.Equal(
            expected.Note,
            actual.Note);
        Assert.Equal(
            expected.Url,
            actual.Url);
        Assert.Equal(
            expected.Price,
            actual.Price);
        Assert.Equal(
            expected.Quantity,
            actual.Quantity);
        Assert.Equal(
            expected.ReservedQuantity,
            actual.ReservedQuantity);
        Assert.Equal(
            expectedCurrentQuantity,
            actual.CurrentParticipantReservedQuantity);
    }
}
