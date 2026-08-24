using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CreateWishlistCommandHandlerTests
{
    private readonly CreateWishlistCommandHandler _handler;
    private readonly Mock<IWishlistService> _wishlistServiceMock;

    public CreateWishlistCommandHandlerTests()
    {
        _wishlistServiceMock = new Mock<IWishlistService>(MockBehavior.Strict);
        _handler = new CreateWishlistCommandHandler(
            _wishlistServiceMock.Object,
            NullLogger<CreateWishlistCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenMemberExists_ReturnsCreatedWishlist()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var eventDate = new DateOnly(
            2026,
            9,
            24);
        var command = new CreateWishlistCommand(
            ownerId,
            "  Liste de Le\u0301a  ",
            WishlistOccasion.Birthday,
            eventDate,
            "  Merci  ");
        WishlistDetails? expected = null;
        _wishlistServiceMock
            .Setup(service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                "Liste de Léa",
                "LISTE DE LÉA",
                WishlistOccasion.Birthday,
                eventDate,
                "Merci",
                cancellationToken))
            .Returns((
                Guid id,
                Guid _,
                string _,
                string _,
                WishlistOccasion _,
                DateOnly? _,
                string? _,
                CancellationToken _) =>
            {
                expected = new WishlistDetails(
                    id,
                    "Liste de Léa",
                    WishlistOccasion.Birthday,
                    eventDate,
                    "Merci",
                    DateTime.UtcNow,
                    null,
                    42);

                return Task.FromResult<WishlistDetails?>(expected);
            });

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _wishlistServiceMock.Verify(
            service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                "Liste de Léa",
                "LISTE DE LÉA",
                WishlistOccasion.Birthday,
                eventDate,
                "Merci",
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberDoesNotExist_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new CreateWishlistCommand(
            ownerId,
            null,
            WishlistOccasion.Other,
            null,
            "   ");
        _wishlistServiceMock
            .Setup(service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                string.Empty,
                string.Empty,
                WishlistOccasion.Other,
                null,
                null,
                cancellationToken))
            .ReturnsAsync((WishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _wishlistServiceMock.Verify(
            service => service.CreateAsync(
                It.Is<Guid>(id => id.Version == 7),
                ownerId,
                string.Empty,
                string.Empty,
                WishlistOccasion.Other,
                null,
                null,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }
}
