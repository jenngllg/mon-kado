using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetSharedWishlistQueryHandlerTests
{
    private readonly GetSharedWishlistQueryHandler _handler;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public GetSharedWishlistQueryHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _handler = new GetSharedWishlistQueryHandler(
            _shareServiceMock.Object,
            NullLogger<GetSharedWishlistQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenShareLinkIsInvalid_ThrowsSharedWishlistNotFoundException()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetSharedWishlistQuery(
            shareLinkId,
            null);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                string.Empty,
                cancellationToken))
            .ReturnsAsync((Application.Models.SharedWishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                string.Empty,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
    }
}
