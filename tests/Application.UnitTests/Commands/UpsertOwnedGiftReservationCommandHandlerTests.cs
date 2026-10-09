using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpsertOwnedGiftReservationCommandHandlerTests
{
    private readonly Mock<IGiftReservationService> _reservationServiceMock;
    private readonly UpsertOwnedGiftReservationCommandHandler _handler;

    public UpsertOwnedGiftReservationCommandHandlerTests()
    {
        _reservationServiceMock = new Mock<IGiftReservationService>(MockBehavior.Strict);
        _handler = new UpsertOwnedGiftReservationCommandHandler(
            _reservationServiceMock.Object,
            NullLogger<UpsertOwnedGiftReservationCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenOwnerRequestsReservation_PassesTrustedOwnershipAndExactCancellationToken()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new UpsertOwnedGiftReservationCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            2,
            42);
        var expected = new GiftReservationMutationResult
        {
            Reservation = new GiftReservationDetails { Id = Guid.CreateVersion7(), WishId = command.WishId, Quantity = 2 },
            IsCreated = true
        };
        _reservationServiceMock
            .Setup(service => service.UpsertAsync(
                It.Is<GiftReservationMutationRequest>(request =>
                    request.IsOwnerReservation &&
                    request.ReservationId.Version == 7 &&
                    request.MemberId == command.OwnerId &&
                    request.WishlistId == command.WishlistId &&
                    request.WishId == command.WishId &&
                    request.Quantity == 2 &&
                    request.ExpectedVersion == 42 &&
                    request.ShareLinkId == Guid.Empty),
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
        _reservationServiceMock.Verify(
            service => service.UpsertAsync(
                It.IsAny<GiftReservationMutationRequest>(),
                cancellationToken),
            Times.Once);
        _reservationServiceMock.VerifyNoOtherCalls();
    }
}
