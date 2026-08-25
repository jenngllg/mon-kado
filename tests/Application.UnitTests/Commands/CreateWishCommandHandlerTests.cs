using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CreateWishCommandHandlerTests
{
    private readonly CreateWishCommandHandler _handler;
    private readonly Mock<IWishService> _wishServiceMock;

    public CreateWishCommandHandlerTests()
    {
        _wishServiceMock = new Mock<IWishService>(MockBehavior.Strict);
        _handler = new CreateWishCommandHandler(
            _wishServiceMock.Object,
            NullLogger<CreateWishCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenWishlistExists_ReturnsNormalizedCreatedWish()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new CreateWishCommand(
            ownerId,
            wishlistId,
            "  Cafe\u0301  ",
            "  Note  ",
            "  https://example.com/gift  ",
            12.34m);
        WishDetails? expected = null;
        _wishServiceMock
            .Setup(service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                wishlistId,
                "Café",
                "Note",
                "https://example.com/gift",
                12.34m,
                cancellationToken))
            .Returns((
                Guid id,
                Guid _,
                Guid _,
                string _,
                string? _,
                string? _,
                decimal? _,
                CancellationToken _) =>
            {
                expected = CreateDetails(id, wishlistId);

                return Task.FromResult<WishDetails?>(expected);
            });

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _wishServiceMock.Verify(
            service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                wishlistId,
                "Café",
                "Note",
                "https://example.com/gift",
                12.34m,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenWishlistIsUnavailable_ThrowsWishlistNotFoundException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new CreateWishCommand(
            ownerId,
            wishlistId,
            null,
            "   ",
            "   ",
            null);
        _wishServiceMock
            .Setup(service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                wishlistId,
                string.Empty,
                null,
                null,
                null,
                cancellationToken))
            .ReturnsAsync((WishDetails?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistNotFoundException>(action);
        _wishServiceMock.Verify(
            service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                wishlistId,
                string.Empty,
                null,
                null,
                null,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }

    private static WishDetails CreateDetails(
        Guid id,
        Guid wishlistId)
    {
        return new WishDetails(
            id,
            wishlistId,
            "Café",
            "Note",
            "https://example.com/gift",
            12.34m,
            1,
            DateTime.UnixEpoch,
            null,
            42);
    }
}
