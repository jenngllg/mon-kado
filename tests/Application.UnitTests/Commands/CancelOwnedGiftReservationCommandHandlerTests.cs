using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CancelOwnedGiftReservationCommandHandlerTests
{
    private readonly Mock<IGiftReservationService> _reservationServiceMock;
    private readonly CancelOwnedGiftReservationCommandHandler _handler;

    public CancelOwnedGiftReservationCommandHandlerTests()
    {
        _reservationServiceMock = new Mock<IGiftReservationService>(MockBehavior.Strict);
        _handler = new CancelOwnedGiftReservationCommandHandler(
            _reservationServiceMock.Object,
            NullLogger<CancelOwnedGiftReservationCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenReservationPresenceVaries_ReturnsExpectedOutcomeAndForwardsToken(bool exists)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new CancelOwnedGiftReservationCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            42);
        _reservationServiceMock
            .Setup(service => service.CancelAsync(
                It.Is<GiftReservationCancellationRequest>(value =>
                    value.IsOwnerReservation && value.MemberId == request.OwnerId &&
                    value.WishlistId == request.WishlistId && value.WishId == request.WishId &&
                    value.ExpectedVersion == 42 && value.ShareLinkId == Guid.Empty),
                cancellationToken))
            .ReturnsAsync(exists);

        // Act
        var action = () => _handler.Handle(
            request,
            cancellationToken);

        // Assert

        if (!exists)
            await Assert.ThrowsAsync<GiftReservationNotFoundException>(action);

        if (exists)
            await action();
        _reservationServiceMock.Verify(
            service => service.CancelAsync(
                It.IsAny<GiftReservationCancellationRequest>(),
                cancellationToken),
            Times.Once);
        _reservationServiceMock.VerifyNoOtherCalls();
    }
}
