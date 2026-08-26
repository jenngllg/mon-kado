using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class JoinSharedWishlistCommandHandlerTests
{
    private readonly JoinSharedWishlistCommandHandler _handler;
    private readonly Mock<IWishlistParticipantService> _participantServiceMock;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public JoinSharedWishlistCommandHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _participantServiceMock = new Mock<IWishlistParticipantService>(MockBehavior.Strict);
        _handler = new JoinSharedWishlistCommandHandler(
            _shareServiceMock.Object,
            _participantServiceMock.Object,
            NullLogger<JoinSharedWishlistCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenShareLinkIsInvalid_ThrowsNotFound(bool secretIsMissing)
    {
        // Arrange
        var command = CreateCommand(secretIsMissing
            ? null
            : "share-secret");
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                command.ShareLinkId,
                command.Secret ?? string.Empty,
                cancellationToken))
            .ReturnsAsync((SharedWishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                command.ShareLinkId,
                command.Secret ?? string.Empty,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenShareLinkIsValid_ReturnsParticipantResult()
    {
        // Arrange
        var command = CreateCommand(null);
        var cancellationToken = TestContext.Current.CancellationToken;
        var wishlist = CreateWishlist();
        var expected = new WishlistParticipantJoinResult(
            new WishlistParticipantDetails(
                Guid.CreateVersion7(),
                "Jenn"),
            true,
            "guest",
            DateTime.UnixEpoch);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                command.ShareLinkId,
                command.Secret ?? string.Empty,
                cancellationToken))
            .ReturnsAsync(wishlist);
        _participantServiceMock
            .Setup(service => service.JoinAsync(
                It.Is<WishlistParticipantJoinRequest>(request => IsExpectedJoinRequest(
                    request,
                    wishlist,
                    command)),
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
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                command.ShareLinkId,
                command.Secret ?? string.Empty,
                cancellationToken),
            Times.Once);
        _participantServiceMock.Verify(
            service => service.JoinAsync(
                It.Is<WishlistParticipantJoinRequest>(request => IsExpectedJoinRequest(
                    request,
                    wishlist,
                    command)),
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenParticipantJoinFails_PropagatesException()
    {
        // Arrange
        var command = CreateCommand();
        var cancellationToken = TestContext.Current.CancellationToken;
        var wishlist = CreateWishlist();
        var completionSource = new TaskCompletionSource<WishlistParticipantJoinResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                command.ShareLinkId,
                command.Secret ?? string.Empty,
                cancellationToken))
            .ReturnsAsync(wishlist);
        _participantServiceMock
            .Setup(service => service.JoinAsync(
                It.Is<WishlistParticipantJoinRequest>(request => IsExpectedJoinRequest(
                    request,
                    wishlist,
                    command)),
                cancellationToken))
            .Returns(completionSource.Task);

        // Act
        var resultTask = _handler.Handle(
            command,
            cancellationToken);
        completionSource.SetException(new WishlistOwnerCannotJoinException());

        // Assert
        await Assert.ThrowsAsync<WishlistOwnerCannotJoinException>(() => resultTask);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                command.ShareLinkId,
                command.Secret ?? string.Empty,
                cancellationToken),
            Times.Once);
        _participantServiceMock.Verify(
            service => service.JoinAsync(
                It.Is<WishlistParticipantJoinRequest>(request => IsExpectedJoinRequest(
                    request,
                    wishlist,
                    command)),
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
    }

    private static JoinSharedWishlistCommand CreateCommand(string? secret = "share-secret")
    {
        return new JoinSharedWishlistCommand(
            Guid.CreateVersion7(),
            secret,
            null,
            "guest-token",
            "Jenn");
    }

    private static SharedWishlistDetails CreateWishlist()
    {
        return new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            []);
    }

    private static bool IsExpectedJoinRequest(
        WishlistParticipantJoinRequest request,
        SharedWishlistDetails wishlist,
        JoinSharedWishlistCommand command)
    {

        return request.ParticipantId.Version == 7 &&
            request.GuestSessionId.Version == 7 &&
            request.WishlistId == wishlist.Id &&
            request.ShareLinkId == command.ShareLinkId &&
            request.ShareSecret == (command.Secret ?? string.Empty) &&
            request.MemberId == command.MemberId &&
            request.GuestToken == command.GuestToken &&
            request.DisplayName == command.DisplayName;
    }
}
