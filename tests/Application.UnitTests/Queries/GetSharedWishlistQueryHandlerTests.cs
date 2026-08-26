using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetSharedWishlistQueryHandlerTests
{
    private readonly GetSharedWishlistQueryHandler _handler;
    private readonly Mock<IWishlistParticipantService> _participantServiceMock;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public GetSharedWishlistQueryHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _participantServiceMock = new Mock<IWishlistParticipantService>(MockBehavior.Strict);
        _handler = new GetSharedWishlistQueryHandler(
            _shareServiceMock.Object,
            _participantServiceMock.Object,
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
            null,
            null,
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
        _participantServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenShareLinkIsValid_ReturnsOptionalCurrentParticipant(bool participantExists)
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetSharedWishlistQuery(
            shareLinkId,
            "secret",
            memberId,
            "guest");
        var wishlist = new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            []);
        var participant = participantExists
            ? new WishlistParticipantDetails(
                Guid.CreateVersion7(),
                "Jenn")
            : null;
        var outcome = participantExists
            ? WishlistParticipantLookupOutcome.Found
            : WishlistParticipantLookupOutcome.NotJoined;
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken))
            .ReturnsAsync(wishlist);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlist.Id,
                memberId,
                "guest",
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                outcome,
                participant));

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Same(
            wishlist,
            result.Wishlist);
        Assert.Same(
            participant,
            result.CurrentParticipant);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken),
            Times.Once);
        _participantServiceMock.Verify(
            service => service.GetCurrentAsync(
                wishlist.Id,
                memberId,
                "guest",
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberNoLongerExists_ThrowsInvalidSession()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetSharedWishlistQuery(
            shareLinkId,
            "secret",
            memberId,
            null);
        var wishlist = new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            []);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken))
            .ReturnsAsync(wishlist);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlist.Id,
                memberId,
                null,
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.MemberNotFound,
                null));

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                shareLinkId,
                "secret",
                cancellationToken),
            Times.Once);
        _participantServiceMock.Verify(
            service => service.GetCurrentAsync(
                wishlist.Id,
                memberId,
                null,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
    }
}
