using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetOwnedGiftReservationQueryHandlerTests
{
    private readonly Mock<IGiftReservationService> _reservationServiceMock;
    private readonly GetOwnedGiftReservationQueryHandler _handler;

    public GetOwnedGiftReservationQueryHandlerTests()
    {
        _reservationServiceMock = new Mock<IGiftReservationService>(MockBehavior.Strict);
        _handler = new GetOwnedGiftReservationQueryHandler(
            _reservationServiceMock.Object,
            NullLogger<GetOwnedGiftReservationQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenReservationPresenceVaries_ReturnsExpectedOutcomeAndForwardsToken(bool exists)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new GetOwnedGiftReservationQuery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        var expected = exists ? new GiftReservationDetails { Id = Guid.CreateVersion7(), WishId = request.WishId, Quantity = 1 } : null;
        _reservationServiceMock
            .Setup(service => service.GetOwnedAsync(
                request.OwnerId,
                request.WishlistId,
                request.WishId,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var action = () => _handler.Handle(
            request,
            cancellationToken);

        // Assert

        if (!exists)
            await Assert.ThrowsAsync<GiftReservationNotFoundException>(action);

        if (exists)
            Assert.Same(
                expected,
                await action());
        _reservationServiceMock.Verify(
            service => service.GetOwnedAsync(
                request.OwnerId,
                request.WishlistId,
                request.WishId,
                cancellationToken),
            Times.Once);
        _reservationServiceMock.VerifyNoOtherCalls();
    }
}
