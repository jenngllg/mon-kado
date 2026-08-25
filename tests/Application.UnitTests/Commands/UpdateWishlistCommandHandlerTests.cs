using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpdateWishlistCommandHandlerTests
{
    private readonly UpdateWishlistCommandHandler _handler;
    private readonly Mock<IWishlistService> _wishlistServiceMock;

    public UpdateWishlistCommandHandlerTests()
    {
        _wishlistServiceMock = new Mock<IWishlistService>(MockBehavior.Strict);
        _handler = new UpdateWishlistCommandHandler(
            _wishlistServiceMock.Object,
            NullLogger<UpdateWishlistCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenWishlistExists_ReturnsNormalizedUpdatedWishlist()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var eventDate = new DateOnly(
            2026,
            9,
            24);
        var command = new UpdateWishlistCommand(
            ownerId,
            wishlistId,
            "  Liste de Le\u0301a  ",
            WishlistOccasion.Birthday,
            eventDate,
            "  Merci  ",
            42);
        var expected = new WishlistDetails(
            wishlistId,
            "Liste de Léa",
            WishlistOccasion.Birthday,
            eventDate,
            "Merci",
            DateTime.UtcNow,
            DateTime.UtcNow,
            43);
        _wishlistServiceMock
            .Setup(service => service.UpdateAsync(
                ownerId,
                wishlistId,
                "Liste de Léa",
                "LISTE DE LÉA",
                WishlistOccasion.Birthday,
                eventDate,
                "Merci",
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
        _wishlistServiceMock.Verify(
            service => service.UpdateAsync(
                ownerId,
                wishlistId,
                "Liste de Léa",
                "LISTE DE LÉA",
                WishlistOccasion.Birthday,
                eventDate,
                "Merci",
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
        var command = new UpdateWishlistCommand(
            ownerId,
            wishlistId,
            null,
            WishlistOccasion.Other,
            null,
            "   ",
            42);
        _wishlistServiceMock
            .Setup(service => service.UpdateAsync(
                ownerId,
                wishlistId,
                string.Empty,
                string.Empty,
                WishlistOccasion.Other,
                null,
                null,
                42,
                cancellationToken))
            .ReturnsAsync((WishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistNotFoundException>(action);
        _wishlistServiceMock.Verify(
            service => service.UpdateAsync(
                ownerId,
                wishlistId,
                string.Empty,
                string.Empty,
                WishlistOccasion.Other,
                null,
                null,
                42,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }
}
