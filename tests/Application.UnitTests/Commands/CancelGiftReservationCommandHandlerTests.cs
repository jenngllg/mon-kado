using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CancelGiftReservationCommandHandlerTests
{
    private readonly CancelGiftReservationCommandHandler _handler;
    private readonly Mock<IGiftReservationService> _reservationServiceMock;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public CancelGiftReservationCommandHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _reservationServiceMock = new Mock<IGiftReservationService>(MockBehavior.Strict);
        _handler = new CancelGiftReservationCommandHandler(
            _shareServiceMock.Object,
            _reservationServiceMock.Object,
            NullLogger<CancelGiftReservationCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenReservationExists_CompletesCancellation()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var wishlist = CreateWishlist();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new CancelGiftReservationCommand(
            shareLinkId,
            "secret",
            wishId,
            memberId,
            "guest",
            42);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken))
            .ReturnsAsync(wishlist);
        _reservationServiceMock
            .Setup(service => service.CancelAsync(
                It.Is<GiftReservationCancellationRequest>(request =>
                    request.ShareLinkId == shareLinkId &&
                    request.ShareSecret == "secret" &&
                    request.WishlistId == wishlist.Id &&
                    request.WishId == wishId &&
                    request.MemberId == memberId &&
                    request.GuestToken == "guest" &&
                    request.ExpectedVersion == 42),
                cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken),
            Times.Once);
        _reservationServiceMock.Verify(
            service => service.CancelAsync(
                It.IsAny<GiftReservationCancellationRequest>(),
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
        var command = new CancelGiftReservationCommand(
            shareLinkId,
            null,
            Guid.CreateVersion7(),
            null,
            null,
            42);
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
    public async Task Handle_WhenReservationDoesNotExist_ThrowsGiftReservationNotFound()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var wishlist = CreateWishlist();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new CancelGiftReservationCommand(
            shareLinkId,
            null,
            wishId,
            null,
            "guest",
            42);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                string.Empty,
                cancellationToken))
            .ReturnsAsync(wishlist);
        _reservationServiceMock
            .Setup(service => service.CancelAsync(
                It.Is<GiftReservationCancellationRequest>(request =>
                    request.ShareSecret == string.Empty &&
                    request.MemberId == null &&
                    request.GuestToken == "guest"),
                cancellationToken))
            .ReturnsAsync(false);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<GiftReservationNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                string.Empty,
                cancellationToken),
            Times.Once);
        _reservationServiceMock.Verify(
            service => service.CancelAsync(
                It.IsAny<GiftReservationCancellationRequest>(),
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
