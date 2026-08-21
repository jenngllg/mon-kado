using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Handlers;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Handlers;

public class RegisterAccountCommandHandlerTests
{
    private readonly RegisterAccountCommandHandler _handler;
    private readonly Mock<IAccountRegistrationService> _registrationServiceMock;

    public RegisterAccountCommandHandlerTests()
    {
        _registrationServiceMock = new Mock<IAccountRegistrationService>(MockBehavior.Strict);
        _handler = new RegisterAccountCommandHandler(_registrationServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenCommandIsValid_TrimsProfileFieldsAndPreservesPassword()
    {
        // Arrange
        const string Password = "  a secure password  ";
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new RegisterAccountCommand(
            " Lea@example.fr ",
            Password,
            " Léa ");
        _registrationServiceMock
            .Setup(service => service.RegisterAsync(
                "Lea@example.fr",
                Password,
                "Léa",
                cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        _registrationServiceMock.Verify(
            service => service.RegisterAsync(
                "Lea@example.fr",
                Password,
                "Léa",
                cancellationToken),
            Times.Once);
        _registrationServiceMock.VerifyNoOtherCalls();
        // Assert
    }
}
