using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CopyWishCommandHandlerTests
{
    private readonly Mock<IWishCopyService> _copyServiceMock;
    private readonly CopyWishCommandHandler _handler;

    public CopyWishCommandHandlerTests()
    {
        _copyServiceMock = new Mock<IWishCopyService>(MockBehavior.Strict);
        _handler = new CopyWishCommandHandler(
            _copyServiceMock.Object,
            NullLogger<CopyWishCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("bearer", "bearer")]
    public async Task Handle_WhenCopyIsRequested_ForwardsSourceAndCancellationWithoutCopyingBrowserMetadata(
        string? secret,
        string expectedSecret)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = TestFixture.Create();
        var expected = fixture.Create<WishDetails>();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var linkId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var request = new CopyWishCommand(
            ownerId,
            wishlistId,
            linkId,
            sourceId,
            secret);
        _copyServiceMock.Setup(service => service.CopyAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                wishlistId,
                linkId,
                sourceId,
                expectedSecret,
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
        _copyServiceMock.Verify(service => service.CopyAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                wishlistId,
                linkId,
                sourceId,
                expectedSecret,
                cancellationToken),
            Times.Once);
        _copyServiceMock.VerifyNoOtherCalls();
    }
}
