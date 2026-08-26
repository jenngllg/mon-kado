using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CreateWishlistShareLinkCommandHandlerTests
{
    private readonly CreateWishlistShareLinkCommandHandler _handler;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public CreateWishlistShareLinkCommandHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _handler = new CreateWishlistShareLinkCommandHandler(
            _shareServiceMock.Object,
            NullLogger<CreateWishlistShareLinkCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenWishlistDoesNotExist_ThrowsWishlistNotFoundException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new CreateWishlistShareLinkCommand(
            ownerId,
            wishlistId);
        _shareServiceMock
            .Setup(service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync((Application.Models.WishlistShareLinkDetails?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
    }
}
