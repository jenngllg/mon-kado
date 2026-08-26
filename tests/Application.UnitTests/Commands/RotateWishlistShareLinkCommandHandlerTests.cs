using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class RotateWishlistShareLinkCommandHandlerTests
{
    private readonly RotateWishlistShareLinkCommandHandler _handler;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public RotateWishlistShareLinkCommandHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _handler = new RotateWishlistShareLinkCommandHandler(
            _shareServiceMock.Object,
            NullLogger<RotateWishlistShareLinkCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenShareLinkDoesNotExist_ThrowsWishlistShareLinkNotFoundException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new RotateWishlistShareLinkCommand(
            ownerId,
            wishlistId,
            42);
        _shareServiceMock
            .Setup(service => service.RotateAsync(
                ownerId,
                wishlistId,
                42,
                cancellationToken))
            .ReturnsAsync((Application.Models.WishlistShareLinkDetails?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistShareLinkNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.RotateAsync(
                ownerId,
                wishlistId,
                42,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
    }
}
