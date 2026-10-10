using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class DeleteWishlistSubscriptionCommandHandlerTests
{
    private readonly Mock<IWishlistSubscriptionService> _serviceMock;
    private readonly DeleteWishlistSubscriptionCommandHandler _handler;

    public DeleteWishlistSubscriptionCommandHandlerTests()
    {
        _serviceMock = new Mock<IWishlistSubscriptionService>(MockBehavior.Strict);
        _handler = new DeleteWishlistSubscriptionCommandHandler(
            _serviceMock.Object,
            NullLogger<DeleteWishlistSubscriptionCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WhenRemovingSubscription_ReportsMissingAndForwardsCancellation(bool removed)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new DeleteWishlistSubscriptionCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        _serviceMock.Setup(service => service.DeleteAsync(
                request.MemberId,
                request.Id,
                cancellationToken))
            .ReturnsAsync(removed);

        // Act
        if (removed)
            Assert.Equal(
                MediatR.Unit.Value,
                await _handler.Handle(
                    request,
                    cancellationToken));

        if (!removed)
            await Assert.ThrowsAsync<WishlistSubscriptionNotFoundException>(() => _handler.Handle(
                request,
                cancellationToken));

        // Assert
        _serviceMock.Verify(service => service.DeleteAsync(
                request.MemberId,
                request.Id,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
