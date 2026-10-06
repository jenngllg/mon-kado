using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistSharePreviewQueryHandlerTests
{
    private readonly Mock<IWishlistSharePreviewService> _serviceMock;
    private readonly GetWishlistSharePreviewQueryHandler _handler;

    public GetWishlistSharePreviewQueryHandlerTests()
    {
        _serviceMock = new Mock<IWishlistSharePreviewService>(MockBehavior.Strict);
        _handler = new GetWishlistSharePreviewQueryHandler(
            _serviceMock.Object,
            NullLogger<GetWishlistSharePreviewQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WhenPublicAccessVaries_ReturnsPreviewOrNotFound(bool available)
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var query = new GetWishlistSharePreviewQuery(Guid.CreateVersion7());
        var preview = available ? new WishlistSharePreview() : null;
        _serviceMock
            .Setup(service => service.GetAsync(
                query.ShareLinkId,
                token))
            .ReturnsAsync(preview);

        // Act
        var action = () => _handler.Handle(
            query,
            token);

        // Assert
        if (available)
            Assert.Same(
                preview,
                await action());

        if (!available)
            await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        _serviceMock.Verify(
            service => service.GetAsync(
                query.ShareLinkId,
                token),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
