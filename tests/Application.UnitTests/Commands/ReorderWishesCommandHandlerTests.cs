using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ReorderWishesCommandHandlerTests
{
    private readonly ReorderWishesCommandHandler _handler;
    private readonly Mock<IWishService> _wishServiceMock;

    public ReorderWishesCommandHandlerTests()
    {
        _wishServiceMock = new Mock<IWishService>(MockBehavior.Strict);
        _handler = new ReorderWishesCommandHandler(
            _wishServiceMock.Object,
            NullLogger<ReorderWishesCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenRequestIsValid_ReturnsUpdatedOrder()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        Guid[] wishIds =
        [
            Guid.CreateVersion7(),
            Guid.CreateVersion7()
        ];
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new ReorderWishesCommand(
            ownerId,
            wishlistId,
            wishIds,
            42);
        var expected = new WishOrderDetails(
            [],
            43);
        _wishServiceMock
            .Setup(service => service.ReorderAsync(
                ownerId,
                wishlistId,
                wishIds,
                42,
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
        _wishServiceMock.Verify(
            service => service.ReorderAsync(
                ownerId,
                wishlistId,
                wishIds,
                42,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }

}
