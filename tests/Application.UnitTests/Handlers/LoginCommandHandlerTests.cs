using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
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
    public async Task Handle_WhenCredentialsAreValid_ReturnsTokensAndPreservesServerValues()
    {
        // Arrange
        const string Password = "  exact password  ";
        const string RefreshToken = "current-refresh-token";
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokens = CreateTokens();
        var command = new LoginCommand(
            " Lea@example.fr ",
            Password,
            rememberMe: true,
            RefreshToken);
        _sessionServiceMock
            .Setup(service => service.LoginAsync(
                "Lea@example.fr",
                Password,
                true,
                RefreshToken,
                cancellationToken))
            .ReturnsAsync(new AccountSessionLoginResult(
                AccountLoginResult.Success,
                tokens));

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            tokens,
            result);
        _sessionServiceMock.Verify(
            service => service.LoginAsync(
                "Lea@example.fr",
                Password,
                true,
                RefreshToken,
                cancellationToken),
            Times.Once);
        _sessionServiceMock.VerifyNoOtherCalls();
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
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new LoginCommand(
            "lea@example.fr",
            "password");
        _sessionServiceMock
            .Setup(service => service.LoginAsync(
                "lea@example.fr",
                "password",
                false,
                null,
                cancellationToken))
            .ReturnsAsync(new AccountSessionLoginResult(
                result,
                null));

        // Act
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => _handler.Handle(
            command,
            cancellationToken));

        // Assert
        Assert.IsType(
            expectedException,
            exception);
        _sessionServiceMock.Verify(
            service => service.LoginAsync(
                "lea@example.fr",
                "password",
                false,
                null,
                cancellationToken),
            Times.Once);
        _sessionServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenSuccessfulLoginHasNoTokens_ThrowsInvalidOperation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new LoginCommand(
            "lea@example.fr",
            "password");
        _sessionServiceMock
            .Setup(service => service.LoginAsync(
                "lea@example.fr",
                "password",
                false,
                null,
                cancellationToken))
            .ReturnsAsync(new AccountSessionLoginResult(
                AccountLoginResult.Success,
                null));

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(action);
        _sessionServiceMock.Verify(
            service => service.LoginAsync(
                "lea@example.fr",
                "password",
                false,
                null,
                cancellationToken),
            Times.Once);
        _sessionServiceMock.VerifyNoOtherCalls();
    }

    private static AccountSessionTokens CreateTokens()
    {
        return new AccountSessionTokens(
            new AccessToken(
                "access-token",
                900),
            "refresh-token",
            new DateTime(
                2030,
                1,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc),
            true);
    }
}
