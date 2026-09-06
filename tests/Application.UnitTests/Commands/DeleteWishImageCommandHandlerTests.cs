using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class DeleteWishImageCommandHandlerTests
{
    private readonly DeleteWishImageCommandHandler _handler;
    private readonly Mock<IWishService> _wishServiceMock;

    public DeleteWishImageCommandHandlerTests()
    {
        _wishServiceMock = new Mock<IWishService>(MockBehavior.Strict);
        _handler = new DeleteWishImageCommandHandler(
            _wishServiceMock.Object,
            NullLogger<DeleteWishImageCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenWishExists_ReturnsUpdatedVersion()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var wish = TestFixture.Create()
            .Create<WishDetails>();
        var command = new DeleteWishImageCommand(
            ownerId,
            wishlistId,
            wishId,
            42);
        _wishServiceMock
            .Setup(service => service.DeleteImageAsync(
                ownerId,
                wishlistId,
                wishId,
                42,
                cancellationToken))
            .ReturnsAsync(wish);

        // Act
        var version = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Equal(
            wish.Version,
            version);
        _wishServiceMock.Verify(
            service => service.DeleteImageAsync(
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
        var command = new DeleteWishImageCommand(
            ownerId,
            wishlistId,
            wishId,
            42);
        _wishServiceMock
            .Setup(service => service.DeleteImageAsync(
                ownerId,
                wishlistId,
                wishId,
                42,
                cancellationToken))
            .ReturnsAsync((WishDetails?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishNotFoundException>(action);
        _wishServiceMock.Verify(
            service => service.DeleteImageAsync(
                ownerId,
                wishlistId,
                wishId,
                42,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }
}
