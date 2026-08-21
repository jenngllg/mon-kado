using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Handlers;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Handlers;

public class RequestEmailConfirmationCommandHandlerTests
{
    private readonly Mock<IEmailConfirmationService> _confirmationServiceMock;
    private readonly RequestEmailConfirmationCommandHandler _handler;

    public RequestEmailConfirmationCommandHandlerTests()
    {
        _confirmationServiceMock = new Mock<IEmailConfirmationService>(MockBehavior.Strict);
        _handler = new RequestEmailConfirmationCommandHandler(_confirmationServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenEmailContainsWhitespace_TrimsEmail()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new RequestEmailConfirmationCommand(" Lea@example.fr ");
        _confirmationServiceMock
            .Setup(service => service.RequestAsync(
                "Lea@example.fr",
                cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        _confirmationServiceMock.Verify(
            service => service.RequestAsync(
                "Lea@example.fr",
                cancellationToken),
            Times.Once);
        _confirmationServiceMock.VerifyNoOtherCalls();
        // Assert
    }
}
