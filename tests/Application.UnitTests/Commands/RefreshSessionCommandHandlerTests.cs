using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Handlers;

public class RefreshSessionCommandHandlerTests
{
    private readonly RefreshSessionCommandHandler _handler;
    private readonly Mock<IAccountSessionService> _sessionServiceMock;

    public RefreshSessionCommandHandlerTests()
    {
        _sessionServiceMock = new Mock<IAccountSessionService>(MockBehavior.Strict);
        _handler = new RefreshSessionCommandHandler(
            _sessionServiceMock.Object,
            NullLogger<RefreshSessionCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenSessionIsValid_ReturnsRotatedTokens()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokens = CreateTokens();
        var command = new RefreshSessionCommand("refresh-token");
        _sessionServiceMock
            .Setup(service => service.RefreshAsync(
                "refresh-token",
                cancellationToken))
            .ReturnsAsync(tokens);

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            tokens,
            result);
        _sessionServiceMock.Verify(service => service.RefreshAsync(
            "refresh-token",
            cancellationToken), Times.Once);
        _sessionServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenSessionIsInvalid_ThrowsInvalidSession()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new RefreshSessionCommand("invalid-refresh-token");
        _sessionServiceMock
            .Setup(service => service.RefreshAsync(
                "invalid-refresh-token",
                cancellationToken))
            .ReturnsAsync((AccountSessionTokens?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _sessionServiceMock.Verify(service => service.RefreshAsync(
            "invalid-refresh-token",
            cancellationToken), Times.Once);
        _sessionServiceMock.VerifyNoOtherCalls();
    }

    private static AccountSessionTokens CreateTokens()
    {
        return new AccountSessionTokens(
            new AccessToken(
                "access-token",
                900),
            "rotated-refresh-token",
            new DateTime(
                2030,
                1,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc),
            false);
    }
}
