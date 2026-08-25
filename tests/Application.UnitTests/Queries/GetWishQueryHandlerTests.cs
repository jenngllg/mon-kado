using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishQueryHandlerTests
{
    private readonly GetWishQueryHandler _handler;
    private readonly Mock<IWishService> _wishServiceMock;

    public GetWishQueryHandlerTests()
    {
        _wishServiceMock = new Mock<IWishService>(MockBehavior.Strict);
        _handler = new GetWishQueryHandler(
            _wishServiceMock.Object,
            NullLogger<GetWishQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenWishExists_ReturnsWish()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetWishQuery(
            memberId,
            wishlistId,
            wishId);
        var expected = CreateDetails(
            wishlistId,
            wishId);
        _wishServiceMock
            .Setup(service => service.GetAsync(
                wishlistId,
                wishId,
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
        _wishServiceMock.Verify(
            service => service.GetAsync(
                wishlistId,
                wishId,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenWishDoesNotExist_ThrowsWishNotFoundException()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetWishQuery(
            Guid.CreateVersion7(),
            wishlistId,
            wishId);
        _wishServiceMock
            .Setup(service => service.GetAsync(
                wishlistId,
                wishId,
                cancellationToken))
            .ReturnsAsync((WishDetails?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishNotFoundException>(action);
        _wishServiceMock.Verify(
            service => service.GetAsync(
                wishlistId,
                wishId,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }

    private static WishDetails CreateDetails(
        Guid wishlistId,
        Guid wishId)
    {
        return new WishDetails(
            wishId,
            wishlistId,
            "Cadeau",
            null,
            null,
            null,
            1,
            DateTime.UnixEpoch,
            null,
            42);
    }
}
