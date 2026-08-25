using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishesQueryHandlerTests
{
    private readonly GetWishesQueryHandler _handler;
    private readonly Mock<IWishService> _wishServiceMock;

    public GetWishesQueryHandlerTests()
    {
        _wishServiceMock = new Mock<IWishService>(MockBehavior.Strict);
        _handler = new GetWishesQueryHandler(
            _wishServiceMock.Object,
            NullLogger<GetWishesQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenCollectionExists_ReturnsCollection()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetWishesQuery(
            ownerId,
            wishlistId);
        var expected = new WishCollectionDetails(
            [],
            42);
        _wishServiceMock
            .Setup(service => service.GetCollectionAsync(
                ownerId,
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
        _wishServiceMock.Verify(
            service => service.GetCollectionAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }
}
