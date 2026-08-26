using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class DeleteWishlistShareLinkCommandHandlerTests
{
    private readonly DeleteWishlistShareLinkCommandHandler _handler;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public DeleteWishlistShareLinkCommandHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _handler = new DeleteWishlistShareLinkCommandHandler(
            _shareServiceMock.Object,
            NullLogger<DeleteWishlistShareLinkCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenShareLinkDoesNotExist_ThrowsWishlistShareLinkNotFoundException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new DeleteWishlistShareLinkCommand(
            ownerId,
            wishlistId,
            42);
        _shareServiceMock
            .Setup(service => service.DeleteAsync(
                ownerId,
                wishlistId,
                42,
                cancellationToken))
            .ReturnsAsync(false);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistShareLinkNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.DeleteAsync(
                ownerId,
                wishlistId,
                42,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
    }
}
