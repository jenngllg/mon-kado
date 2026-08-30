using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetCurrentGiftReservationQueryHandlerTests
{
    private readonly GetCurrentGiftReservationQueryHandler _handler;
    private readonly Mock<IWishlistParticipantService> _participantServiceMock;
    private readonly Mock<IGiftReservationService> _reservationServiceMock;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public GetCurrentGiftReservationQueryHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _participantServiceMock = new Mock<IWishlistParticipantService>(MockBehavior.Strict);
        _reservationServiceMock = new Mock<IGiftReservationService>(MockBehavior.Strict);
        _handler = new GetCurrentGiftReservationQueryHandler(
            _shareServiceMock.Object,
            _participantServiceMock.Object,
            _reservationServiceMock.Object,
            NullLogger<GetCurrentGiftReservationQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenReservationExists_ReturnsReservation()
    {
        // Arrange
        var data = CreateData();
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Jenn");
        var expected = new GiftReservationDetails
        {
            Id = Guid.CreateVersion7(),
            WishId = data.WishId,
            Quantity = 2
        };
        SetupShareAndParticipant(
            data,
            new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.Found,
                participant));
        _reservationServiceMock
            .Setup(service => service.GetAsync(
                data.Wishlist.Id,
                data.WishId,
                participant.Id,
                data.CancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            data.Query,
            data.CancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        VerifyShareAndParticipant(data);
        _reservationServiceMock.Verify(
            service => service.GetAsync(
                data.Wishlist.Id,
                data.WishId,
                participant.Id,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenReservationDoesNotExist_ThrowsGiftReservationNotFound()
    {
        // Arrange
        var data = CreateData();
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Jenn");
        SetupShareAndParticipant(
            data,
            new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.Found,
                participant));
        _reservationServiceMock
            .Setup(service => service.GetAsync(
                data.Wishlist.Id,
                data.WishId,
                participant.Id,
                data.CancellationToken))
            .ReturnsAsync((GiftReservationDetails?)null);

        // Act
        var action = () => _handler.Handle(
            data.Query,
            data.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GiftReservationNotFoundException>(action);
        VerifyShareAndParticipant(data);
        _reservationServiceMock.Verify(
            service => service.GetAsync(
                data.Wishlist.Id,
                data.WishId,
                participant.Id,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistParticipantLookupOutcome.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistParticipantLookupOutcome.MissingIdentity, typeof(GuestSessionInvalidException))]
    [InlineData(WishlistParticipantLookupOutcome.InvalidGuestSession, typeof(GuestSessionInvalidException))]
    [InlineData(WishlistParticipantLookupOutcome.NotJoined, typeof(WishlistParticipantNotFoundException))]
    [InlineData(WishlistParticipantLookupOutcome.Found, typeof(WishlistParticipantNotFoundException))]
    public async Task Handle_WhenParticipantCannotBeResolved_ThrowsExpectedException(
        WishlistParticipantLookupOutcome outcome,
        Type expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        SetupShareAndParticipant(
            data,
            new WishlistParticipantLookupResult(
                outcome,
                null));

        // Act
        var thrown = await Record.ExceptionAsync(() => _handler.Handle(
            data.Query,
            data.CancellationToken));

        // Assert
        Assert.IsType(
            expectedExceptionType,
            thrown);
        VerifyShareAndParticipant(data);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenShareLinkIsInvalid_ThrowsSharedWishlistNotFound()
    {
        // Arrange
        var data = CreateData();
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                data.Query.ShareLinkId,
                "secret",
                data.CancellationToken))
            .ReturnsAsync((SharedWishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            data.Query,
            data.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                data.Query.ShareLinkId,
                "secret",
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenSecretIsNull_UsesEmptySecret()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetCurrentGiftReservationQuery(
            Guid.CreateVersion7(),
            null,
            Guid.CreateVersion7(),
            null,
            null);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                query.ShareLinkId,
                string.Empty,
                cancellationToken))
            .ReturnsAsync((SharedWishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                query.ShareLinkId,
                string.Empty,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void SetupShareAndParticipant(
        (
            SharedWishlistDetails Wishlist,
            Guid WishId,
            GetCurrentGiftReservationQuery Query,
            CancellationToken CancellationToken) data,
        WishlistParticipantLookupResult lookup)
    {
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                data.Query.ShareLinkId,
                "secret",
                data.CancellationToken))
            .ReturnsAsync(data.Wishlist);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                data.Wishlist.Id,
                data.Query.MemberId,
                data.Query.GuestToken,
                data.CancellationToken))
            .ReturnsAsync(lookup);
    }

    private void VerifyShareAndParticipant((
        SharedWishlistDetails Wishlist,
        Guid WishId,
        GetCurrentGiftReservationQuery Query,
        CancellationToken CancellationToken) data)
    {
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                data.Query.ShareLinkId,
                "secret",
                data.CancellationToken),
            Times.Once);
        _participantServiceMock.Verify(
            service => service.GetCurrentAsync(
                data.Wishlist.Id,
                data.Query.MemberId,
                data.Query.GuestToken,
                data.CancellationToken),
            Times.Once);
    }

    private void VerifyNoOtherCalls()
    {
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    private static (
        SharedWishlistDetails Wishlist,
        Guid WishId,
        GetCurrentGiftReservationQuery Query,
        CancellationToken CancellationToken) CreateData()
    {
        var wishlist = new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            []);
        var wishId = Guid.CreateVersion7();
        var query = new GetCurrentGiftReservationQuery(
            Guid.CreateVersion7(),
            "secret",
            wishId,
            Guid.CreateVersion7(),
            "guest");

        return (
            wishlist,
            wishId,
            query,
            TestContext.Current.CancellationToken);
    }
}
