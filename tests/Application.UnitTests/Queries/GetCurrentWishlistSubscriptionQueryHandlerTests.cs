using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetCurrentWishlistSubscriptionQueryHandlerTests
{
    private readonly Mock<IWishlistSubscriptionService> _serviceMock;
    private readonly GetCurrentWishlistSubscriptionQueryHandler _handler;

    public GetCurrentWishlistSubscriptionQueryHandlerTests()
    {
        _serviceMock = new Mock<IWishlistSubscriptionService>(MockBehavior.Strict);
        _handler = new GetCurrentWishlistSubscriptionQueryHandler(
            _serviceMock.Object,
            NullLogger<GetCurrentWishlistSubscriptionQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(false, "secret")]
    [InlineData(true, "secret")]
    [InlineData(false, null)]
    public async Task Handle_WhenServiceResponds_ForwardsCancellationAndPreservesResult(
        bool absent,
        string? secret)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberId = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        var request = new GetCurrentWishlistSubscriptionQuery(
            memberId,
            id,
            secret);
        var expected = WishlistSubscriptionTestData.CreateDetails();
        _serviceMock.Setup(service => service.GetCurrentAsync(
            memberId,
            id,
            secret ?? string.Empty,
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
        _serviceMock.Verify(service => service.GetCurrentAsync(
            memberId,
            id,
            secret ?? string.Empty,
            cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
