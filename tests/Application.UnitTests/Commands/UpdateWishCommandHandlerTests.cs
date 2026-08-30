using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpdateWishCommandHandlerTests
{
    private readonly UpdateWishCommandHandler _handler;
    private readonly Mock<IWishService> _wishServiceMock;

    public UpdateWishCommandHandlerTests()
    {
        _wishServiceMock = new Mock<IWishService>(MockBehavior.Strict);
        _handler = new UpdateWishCommandHandler(
            _wishServiceMock.Object,
            NullLogger<UpdateWishCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenWishExists_ReturnsNormalizedUpdatedWish()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new UpdateWishCommand(
            ownerId,
            wishlistId,
            wishId,
            "  Cafe\u0301  ",
            "   ",
            "  https://example.com/gift  ",
            12.34m,
            42,
            4);
        var expected = new WishDetails(
            wishId,
            wishlistId,
            "Café",
            null,
            "https://example.com/gift",
            12.34m,
            3,
            DateTime.UtcNow,
            DateTime.UtcNow,
            43,
            4);
        _wishServiceMock
            .Setup(service => service.UpdateAsync(
                ownerId,
                wishlistId,
                wishId,
                "Café",
                null,
                "https://example.com/gift",
                12.34m,
                4,
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
            service => service.UpdateAsync(
                ownerId,
                wishlistId,
                wishId,
                "Café",
                null,
                "https://example.com/gift",
                12.34m,
                4,
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
        var command = new UpdateWishCommand(
            ownerId,
            wishlistId,
            wishId,
            null,
            null,
            null,
            null,
            42);
        _wishServiceMock
            .Setup(service => service.UpdateAsync(
                ownerId,
                wishlistId,
                wishId,
                string.Empty,
                null,
                null,
                null,
                1,
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
            service => service.UpdateAsync(
                ownerId,
                wishlistId,
                wishId,
                string.Empty,
                null,
                null,
                null,
                1,
                42,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }
}
