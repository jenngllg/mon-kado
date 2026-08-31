using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetSharedWishlistQueryHandlerTests
{
    private readonly GetSharedWishlistQueryHandler _handler;
    private readonly Mock<IGiftReservationService> _reservationServiceMock;
    private readonly Mock<IWishlistParticipantService> _participantServiceMock;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public GetSharedWishlistQueryHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _participantServiceMock = new Mock<IWishlistParticipantService>(MockBehavior.Strict);
        _reservationServiceMock = new Mock<IGiftReservationService>(MockBehavior.Strict);
        _handler = new GetSharedWishlistQueryHandler(
            _shareServiceMock.Object,
            _participantServiceMock.Object,
            _reservationServiceMock.Object,
            NullLogger<GetSharedWishlistQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenShareLinkIsInvalid_ThrowsSharedWishlistNotFoundException()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetSharedWishlistQuery(
            shareLinkId,
            null,
            null,
            null,
            availableOnly: false);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                string.Empty,
                cancellationToken))
            .ReturnsAsync((Application.Models.SharedWishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                string.Empty,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenShareLinkIsValid_ReturnsOptionalCurrentParticipant(bool participantExists)
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetSharedWishlistQuery(
            shareLinkId,
            "secret",
            memberId,
            "guest",
            availableOnly: false);
        var wish = new SharedWishDetails(
            Guid.CreateVersion7(),
            "Gift",
            null,
            null,
            3,
            2);
        var wishlist = new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            [wish]);
        var participant = participantExists
            ? new WishlistParticipantDetails(
                Guid.CreateVersion7(),
                "Jenn")
            : null;
        var outcome = participantExists
            ? WishlistParticipantLookupOutcome.Found
            : WishlistParticipantLookupOutcome.NotJoined;
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken))
            .ReturnsAsync(wishlist);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlist.Id,
                memberId,
                "guest",
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                outcome,
                participant));

        if (participant is not null)
        {
            _reservationServiceMock
                .Setup(service => service.GetQuantitiesAsync(
                    wishlist.Id,
                    participant.Id,
                    cancellationToken))
                .ReturnsAsync(new Dictionary<Guid, int>
                {
                    [wish.Id] = 1
                });
        }

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Equal(
            wishlist.Id,
            result.Wishlist.Id);
        Assert.Same(
            participant,
            result.CurrentParticipant);
        var enrichedWish = Assert.Single(result.Wishlist.Wishes);
        Assert.Equal(
            wish.Id,
            enrichedWish.Id);
        Assert.Equal(
            3,
            enrichedWish.Quantity);
        Assert.Equal(
            2,
            enrichedWish.ReservedQuantity);
        Assert.Equal(
            participantExists ? 1 : null,
            enrichedWish.CurrentParticipantReservedQuantity);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken),
            Times.Once);
        _participantServiceMock.Verify(
            service => service.GetCurrentAsync(
                wishlist.Id,
                memberId,
                "guest",
                cancellationToken),
            Times.Once);

        if (participant is not null)
        {
            _reservationServiceMock.Verify(
                service => service.GetQuantitiesAsync(
                    wishlist.Id,
                    participant.Id,
                    cancellationToken),
                Times.Once);
        }

        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenAvailableOnlyIsTrue_ReturnsAvailableAndCurrentlyReservedWishesInOriginalOrder()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var availableWish = new SharedWishDetails(
            Guid.CreateVersion7(),
            "Available gift",
            null,
            null,
            2,
            1);
        var currentlyReservedWish = new SharedWishDetails(
            Guid.CreateVersion7(),
            "Currently reserved gift",
            null,
            null,
            1,
            1);
        var unavailableWish = new SharedWishDetails(
            Guid.CreateVersion7(),
            "Unavailable gift",
            null,
            null,
            1,
            1);
        var overreservedWish = new SharedWishDetails(
            Guid.CreateVersion7(),
            "Overreserved gift",
            null,
            null,
            1,
            2);
        var wishlist = new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            [
                availableWish,
                currentlyReservedWish,
                unavailableWish,
                overreservedWish
            ]);
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Participant");
        var query = new GetSharedWishlistQuery(
            shareLinkId,
            "secret",
            null,
            "guest",
            availableOnly: true);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken))
            .ReturnsAsync(wishlist);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlist.Id,
                null,
                "guest",
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.Found,
                participant));
        _reservationServiceMock
            .Setup(service => service.GetQuantitiesAsync(
                wishlist.Id,
                participant.Id,
                cancellationToken))
            .ReturnsAsync(new Dictionary<Guid, int>
            {
                [currentlyReservedWish.Id] = 1
            });

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Equal(
            [
                availableWish.Id,
                currentlyReservedWish.Id
            ],
            result.Wishlist.Wishes.Select(wish => wish.Id));
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken),
            Times.Once);
        _participantServiceMock.Verify(
            service => service.GetCurrentAsync(
                wishlist.Id,
                null,
                "guest",
                cancellationToken),
            Times.Once);
        _reservationServiceMock.Verify(
            service => service.GetQuantitiesAsync(
                wishlist.Id,
                participant.Id,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberNoLongerExists_ThrowsInvalidSession()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetSharedWishlistQuery(
            shareLinkId,
            "secret",
            memberId,
            null,
            availableOnly: false);
        var wishlist = new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            []);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken))
            .ReturnsAsync(wishlist);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlist.Id,
                memberId,
                null,
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
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken),
            Times.Once);
        _participantServiceMock.Verify(
            service => service.GetCurrentAsync(
                wishlist.Id,
                memberId,
                null,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }
}
