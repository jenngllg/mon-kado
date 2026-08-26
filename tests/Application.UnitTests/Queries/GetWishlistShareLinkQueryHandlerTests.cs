using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistShareLinkQueryHandlerTests
{
    private readonly GetWishlistShareLinkQueryHandler _handler;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public GetWishlistShareLinkQueryHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _handler = new GetWishlistShareLinkQueryHandler(
            _shareServiceMock.Object,
            NullLogger<GetWishlistShareLinkQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenShareLinkDoesNotExist_ThrowsWishlistShareLinkNotFoundException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetWishlistShareLinkQuery(
            ownerId,
            wishlistId);
        _shareServiceMock
            .Setup(service => service.GetAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync((Application.Models.WishlistShareLinkDetails?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistShareLinkNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.GetAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
    }
}
