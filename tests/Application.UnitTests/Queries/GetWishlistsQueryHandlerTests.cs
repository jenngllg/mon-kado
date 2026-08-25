using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistsQueryHandlerTests
{
    private readonly GetWishlistsQueryHandler _handler;
    private readonly Mock<IWishlistService> _wishlistServiceMock;

    public GetWishlistsQueryHandlerTests()
    {
        _wishlistServiceMock = new Mock<IWishlistService>(MockBehavior.Strict);
        _handler = new GetWishlistsQueryHandler(
            _wishlistServiceMock.Object,
            NullLogger<GetWishlistsQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenMemberExists_ReturnsOwnedWishlists()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetWishlistsQuery(memberId);
        IReadOnlyCollection<WishlistDetails> expected =
        [
            new WishlistDetails(
                Guid.CreateVersion7(),
                "Liste récente",
                WishlistOccasion.Birthday,
                null,
                null,
                new DateTime(
                    2026,
                    8,
                    25,
                    12,
                    0,
                    0,
                    DateTimeKind.Utc),
                null,
                42)
        ];
        _wishlistServiceMock
            .Setup(service => service.GetByOwnerIdAsync(
                memberId,
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
        _wishlistServiceMock.Verify(
            service => service.GetByOwnerIdAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberDoesNotExist_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetWishlistsQuery(memberId);
        _wishlistServiceMock
            .Setup(service => service.GetByOwnerIdAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync((IReadOnlyCollection<WishlistDetails>?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _wishlistServiceMock.Verify(
            service => service.GetByOwnerIdAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }
}
