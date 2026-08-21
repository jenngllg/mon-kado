using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Handlers;

public class LoginCommandHandlerTests
{
    private readonly LoginCommandHandler _handler;
    private readonly Mock<IAccountSessionService> _sessionServiceMock;

    public LoginCommandHandlerTests()
    {
        _sessionServiceMock = new Mock<IAccountSessionService>(MockBehavior.Strict);
        _handler = new LoginCommandHandler(_sessionServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenCredentialsAreValid_TrimsEmailAndPreservesOtherValues()
    {
        // Arrange
        const string Password = "  exact password  ";
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new LoginCommand(
            " Lea@example.fr ",
            Password,
            rememberMe: true);
        _sessionServiceMock
            .Setup(service => service.LoginAsync(
                "Lea@example.fr",
                Password,
                true,
                cancellationToken))
            .ReturnsAsync(AccountLoginResult.Success);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        _sessionServiceMock.Verify(
            service => service.LoginAsync(
                "Lea@example.fr",
                Password,
                true,
                cancellationToken),
            Times.Once);
        _sessionServiceMock.VerifyNoOtherCalls();
        // Assert
    }

    [Theory]
    [InlineData(
        AccountLoginResult.InvalidCredentials,
        typeof(InvalidCredentialsException))]
    [InlineData(
        AccountLoginResult.EmailNotConfirmed,
        typeof(EmailNotConfirmedException))]
    public async Task Handle_WhenLoginFails_ThrowsPublicException(
        AccountLoginResult result,
        Type expectedException)
    {
        // Arrange
        // Act
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new LoginCommand(
            "lea@example.fr",
            "password");
        _sessionServiceMock
            .Setup(service => service.LoginAsync(
                "lea@example.fr",
                "password",
                false,
                cancellationToken))
            .ReturnsAsync(result);

        // Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => _handler.Handle(
            command,
            cancellationToken));

        Assert.IsType(
            expectedException,
            exception);
        _sessionServiceMock.Verify(
            service => service.LoginAsync(
                "lea@example.fr",
                "password",
                false,
                cancellationToken),
            Times.Once);
        _sessionServiceMock.VerifyNoOtherCalls();
    }
}
