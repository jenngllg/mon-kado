using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class SetWishlistArchivedCommandHandlerTests
{
    private readonly Mock<IWishlistService> _wishlistServiceMock;
    private readonly SetWishlistArchivedCommandHandler _handler;

    public SetWishlistArchivedCommandHandlerTests()
    {
        _wishlistServiceMock = new Mock<IWishlistService>(MockBehavior.Strict);
        _handler = new SetWishlistArchivedCommandHandler(
            _wishlistServiceMock.Object,
            NullLogger<SetWishlistArchivedCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WhenStateIsRequested_ReturnsServiceResultAndTransmitsToken(bool isArchived)
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new SetWishlistArchivedCommand(
            ownerId,
            wishlistId,
            isArchived,
            42);
        var expected = new WishlistDetails(
            wishlistId,
            "Liste",
            WishlistOccasion.Other,
            null,
            null,
            new DateTime(
                2026,
                10,
                6,
                0,
                0,
                0,
                DateTimeKind.Utc),
            null,
            43)
        {
            IsArchived = isArchived
        };
        _wishlistServiceMock
            .Setup(service => service.SetArchivedAsync(
                ownerId,
                wishlistId,
                isArchived,
                42,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _wishlistServiceMock.Verify(
            service => service.SetArchivedAsync(
                ownerId,
                wishlistId,
                isArchived,
                42,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }
}
