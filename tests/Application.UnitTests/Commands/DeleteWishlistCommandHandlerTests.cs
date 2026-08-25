using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class DeleteWishlistCommandHandlerTests
{
    private readonly DeleteWishlistCommandHandler _handler;
    private readonly Mock<IWishlistService> _wishlistServiceMock;

    public DeleteWishlistCommandHandlerTests()
    {
        _wishlistServiceMock = new Mock<IWishlistService>(MockBehavior.Strict);
        _handler = new DeleteWishlistCommandHandler(
            _wishlistServiceMock.Object,
            NullLogger<DeleteWishlistCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenWishlistExists_CompletesDeletion()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new DeleteWishlistCommand(
            ownerId,
            wishlistId,
            42);
        _wishlistServiceMock
            .Setup(service => service.DeleteAsync(
                ownerId,
                wishlistId,
                42,
                cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _wishlistServiceMock.Verify(
            service => service.DeleteAsync(
                ownerId,
                wishlistId,
                42,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenWishlistDoesNotExist_ThrowsWishlistNotFoundException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new DeleteWishlistCommand(
            ownerId,
            wishlistId,
            42);
        _wishlistServiceMock
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
        await Assert.ThrowsAsync<WishlistNotFoundException>(action);
        _wishlistServiceMock.Verify(
            service => service.DeleteAsync(
                ownerId,
                wishlistId,
                42,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }
}
