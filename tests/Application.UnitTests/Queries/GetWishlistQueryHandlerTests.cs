using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistQueryHandlerTests
{
    private readonly GetWishlistQueryHandler _handler;
    private readonly Mock<IWishlistService> _wishlistServiceMock;

    public GetWishlistQueryHandlerTests()
    {
        _wishlistServiceMock = new Mock<IWishlistService>(MockBehavior.Strict);
        _handler = new GetWishlistQueryHandler(
            _wishlistServiceMock.Object,
            NullLogger<GetWishlistQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenWishlistExists_ReturnsWishlist()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetWishlistQuery(
            memberId,
            wishlistId);
        var expected = new WishlistDetails(
            wishlistId,
            "Liste",
            WishlistOccasion.Other,
            null,
            null,
            DateTime.UtcNow,
            null,
            42);
        _wishlistServiceMock
            .Setup(service => service.GetAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _wishlistServiceMock.Verify(
            service => service.GetAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenWishlistDoesNotExist_ThrowsWishlistNotFoundException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetWishlistQuery(
            memberId,
            wishlistId);
        _wishlistServiceMock
            .Setup(service => service.GetAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync((WishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistNotFoundException>(action);
        _wishlistServiceMock.Verify(
            service => service.GetAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }
}
