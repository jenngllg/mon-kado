using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Handlers;

public class LogoutCommandHandlerTests
{
    private readonly LogoutCommandHandler _handler;
    private readonly Mock<IAccountSessionService> _sessionServiceMock;

    public LogoutCommandHandlerTests()
    {
        _sessionServiceMock = new Mock<IAccountSessionService>(MockBehavior.Strict);
        _handler = new LogoutCommandHandler(
            _sessionServiceMock.Object,
            NullLogger<LogoutCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("refresh-token")]
    public async Task Handle_WhenLogoutIsRequested_DelegatesToSessionService(
        string? refreshToken)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new LogoutCommand(refreshToken);
        _sessionServiceMock
            .Setup(service => service.LogoutAsync(
                refreshToken,
                cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _sessionServiceMock.Verify(
            service => service.LogoutAsync(
                refreshToken,
                cancellationToken),
            Times.Once);
        _sessionServiceMock.VerifyNoOtherCalls();
    }
}
