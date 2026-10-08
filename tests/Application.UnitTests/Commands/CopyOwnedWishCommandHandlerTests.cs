using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CopyOwnedWishCommandHandlerTests
{
    private readonly Mock<IWishCopyService> _copyServiceMock;
    private readonly CopyOwnedWishCommandHandler _handler;

    public CopyOwnedWishCommandHandlerTests()
    {
        _copyServiceMock = new Mock<IWishCopyService>(MockBehavior.Strict);
        _handler = new CopyOwnedWishCommandHandler(
            _copyServiceMock.Object,
            NullLogger<CopyOwnedWishCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenOwnedCopyIsRequested_ForwardsIdentifiersAndCancellation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = TestFixture.Create();
        var expected = fixture.Create<WishDetails>();
        var request = new CopyOwnedWishCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        _copyServiceMock.Setup(service => service.CopyOwnedAsync(
                It.Is<Guid>(id => id.Version == 7),
                request.OwnerId,
                request.DestinationWishlistId.GetValueOrDefault(),
                request.SourceWishlistId,
                request.SourceWishId,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            request,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _copyServiceMock.Verify(service => service.CopyOwnedAsync(
                It.Is<Guid>(id => id.Version == 7),
                request.OwnerId,
                request.DestinationWishlistId.GetValueOrDefault(),
                request.SourceWishlistId,
                request.SourceWishId,
                cancellationToken),
            Times.Once);
        _copyServiceMock.VerifyNoOtherCalls();
    }
}
