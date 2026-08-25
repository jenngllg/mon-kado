using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class DeleteWishCommandHandlerTests
{
    private readonly DeleteWishCommandHandler _handler;
    private readonly Mock<IWishService> _wishServiceMock;

    public DeleteWishCommandHandlerTests()
    {
        _wishServiceMock = new Mock<IWishService>(MockBehavior.Strict);
        _handler = new DeleteWishCommandHandler(
            _wishServiceMock.Object,
            NullLogger<DeleteWishCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenWishExists_CompletesDeletion()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new DeleteWishCommand(
            ownerId,
            wishlistId,
            wishId,
            42);
        _wishServiceMock
            .Setup(service => service.DeleteAsync(
                ownerId,
                wishlistId,
                wishId,
                42,
                cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _wishServiceMock.Verify(
            service => service.DeleteAsync(
                ownerId,
                wishlistId,
                wishId,
                42,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenWishDoesNotExist_ThrowsWishNotFoundException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new DeleteWishCommand(
            ownerId,
            wishlistId,
            wishId,
            42);
        _wishServiceMock
            .Setup(service => service.DeleteAsync(
                ownerId,
                wishlistId,
                wishId,
                42,
                cancellationToken))
            .ReturnsAsync(false);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishNotFoundException>(action);
        _wishServiceMock.Verify(
            service => service.DeleteAsync(
                ownerId,
                wishlistId,
                wishId,
                42,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }
}
