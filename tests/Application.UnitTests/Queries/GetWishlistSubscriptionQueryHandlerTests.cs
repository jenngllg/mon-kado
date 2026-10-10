using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistSubscriptionQueryHandlerTests
{
    private readonly Mock<IWishlistSubscriptionService> _serviceMock;
    private readonly GetWishlistSubscriptionQueryHandler _handler;

    public GetWishlistSubscriptionQueryHandlerTests()
    {
        _serviceMock = new Mock<IWishlistSubscriptionService>(MockBehavior.Strict);
        _handler = new GetWishlistSubscriptionQueryHandler(
            _serviceMock.Object,
            NullLogger<GetWishlistSubscriptionQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenServiceResponds_ForwardsCancellationAndPreservesResult(bool absent)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberId = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        var request = new GetWishlistSubscriptionQuery(
            memberId,
            id);
        var expected = WishlistSubscriptionTestData.CreateDetails();
        _serviceMock.Setup(service => service.GetAsync(
            memberId,
            id,
            cancellationToken))
            .ReturnsAsync(absent ? null : expected);

        // Act
        if (absent)
            await Assert.ThrowsAsync<WishlistSubscriptionNotFoundException>(() => _handler.Handle(
                request,
                cancellationToken));

        if (!absent)
        {
            var result = await _handler.Handle(
                request,
                cancellationToken);
            Assert.Same(
                expected,
                result);
        }

        // Assert
        _serviceMock.Verify(service => service.GetAsync(
            memberId,
            id,
            cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
