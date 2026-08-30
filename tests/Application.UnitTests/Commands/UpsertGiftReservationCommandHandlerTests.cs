using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpsertGiftReservationCommandHandlerTests
{
    private readonly UpsertGiftReservationCommandHandler _handler;
    private readonly Mock<IGiftReservationService> _reservationServiceMock;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public UpsertGiftReservationCommandHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _reservationServiceMock = new Mock<IGiftReservationService>(MockBehavior.Strict);
        _handler = new UpsertGiftReservationCommandHandler(
            _shareServiceMock.Object,
            _reservationServiceMock.Object,
            NullLogger<UpsertGiftReservationCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenShareLinkIsValid_ReturnsReservationResult()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlist = CreateWishlist();
        var expected = new GiftReservationMutationResult
        {
            Reservation = new GiftReservationDetails
            {
                Id = Guid.CreateVersion7(),
                WishId = wishId,
                Quantity = 2
            },
            IsCreated = true
        };
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new UpsertGiftReservationCommand(
            shareLinkId,
            "secret",
            wishId,
            memberId,
            "guest",
            2,
            42);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken))
            .ReturnsAsync(wishlist);
        _reservationServiceMock
            .Setup(service => service.UpsertAsync(
                It.Is<GiftReservationMutationRequest>(request =>
                    request.ReservationId.Version == 7 &&
                    request.ShareLinkId == shareLinkId &&
                    request.ShareSecret == "secret" &&
                    request.WishlistId == wishlist.Id &&
                    request.WishId == wishId &&
                    request.MemberId == memberId &&
                    request.GuestToken == "guest" &&
                    request.Quantity == 2 &&
                    request.ExpectedVersion == 42),
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken),
            Times.Once);
        _reservationServiceMock.Verify(
            service => service.UpsertAsync(
                It.IsAny<GiftReservationMutationRequest>(),
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenShareLinkIsInvalid_ThrowsSharedWishlistNotFound()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new UpsertGiftReservationCommand(
            shareLinkId,
            null,
            Guid.CreateVersion7(),
            null,
            null,
            null,
            null);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                string.Empty,
                cancellationToken))
            .ReturnsAsync((SharedWishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            command,
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
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenQuantityIsNull_PassesZeroToReservationService()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var wishlist = CreateWishlist();
        var expected = new GiftReservationMutationResult
        {
            Reservation = new GiftReservationDetails
            {
                Id = Guid.CreateVersion7(),
                WishId = wishId,
                Quantity = 0
            },
            IsCreated = true
        };
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new UpsertGiftReservationCommand(
            shareLinkId,
            null,
            wishId,
            Guid.CreateVersion7(),
            null,
            null,
            null);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                string.Empty,
                cancellationToken))
            .ReturnsAsync(wishlist);
        _reservationServiceMock
            .Setup(service => service.UpsertAsync(
                It.Is<GiftReservationMutationRequest>(request =>
                    request.ShareSecret == string.Empty &&
                    request.Quantity == 0),
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                string.Empty,
                cancellationToken),
            Times.Once);
        _reservationServiceMock.Verify(
            service => service.UpsertAsync(
                It.Is<GiftReservationMutationRequest>(request =>
                    request.ShareSecret == string.Empty &&
                    request.Quantity == 0),
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _reservationServiceMock.VerifyNoOtherCalls();
    }

    private static SharedWishlistDetails CreateWishlist()
    {
        return new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            []);
    }
}
