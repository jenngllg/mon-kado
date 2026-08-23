using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class RequestPasswordResetCommandHandlerTests
{
    private readonly RequestPasswordResetCommandHandler _handler;
    private readonly Mock<IPasswordResetService> _passwordResetServiceMock;

    public RequestPasswordResetCommandHandlerTests()
    {
        _passwordResetServiceMock = new Mock<IPasswordResetService>(MockBehavior.Strict);
        _handler = new RequestPasswordResetCommandHandler(
            _passwordResetServiceMock.Object,
            NullLogger<RequestPasswordResetCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenEmailContainsWhitespace_TrimsEmailAndRequestsReset()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new RequestPasswordResetCommand(" Jenn@example.fr ");
        _passwordResetServiceMock
            .Setup(service => service.RequestAsync(
                "Jenn@example.fr",
                cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _passwordResetServiceMock.Verify(
            service => service.RequestAsync(
                "Jenn@example.fr",
                cancellationToken),
            Times.Once);
        _passwordResetServiceMock.VerifyNoOtherCalls();
    }
}
