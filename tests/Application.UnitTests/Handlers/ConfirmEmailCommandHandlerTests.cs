using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Handlers;

public class ConfirmEmailCommandHandlerTests
{
    private readonly Mock<IEmailConfirmationService> _confirmationServiceMock;
    private readonly ConfirmEmailCommandHandler _handler;

    public ConfirmEmailCommandHandlerTests()
    {
        _confirmationServiceMock = new Mock<IEmailConfirmationService>(MockBehavior.Strict);
        _handler = new ConfirmEmailCommandHandler(
            _confirmationServiceMock.Object,
            NullLogger<ConfirmEmailCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenConfirmationIsValid_PreservesToken()
    {
        // Arrange
        var userId = Guid.CreateVersion7().ToString("D");
        const string Token = "AbCd_-0123";
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new ConfirmEmailCommand(
            userId,
            Token);
        _confirmationServiceMock
            .Setup(service => service.ConfirmAsync(
                userId,
                Token,
                cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _confirmationServiceMock.Verify(
            service => service.ConfirmAsync(
                userId,
                Token,
                cancellationToken),
            Times.Once);
        _confirmationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenConfirmationFails_ThrowsGenericException()
    {
        // Arrange
        // Act
        var userId = Guid.CreateVersion7().ToString("D");
        const string Token = "dG9rZW4";
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new ConfirmEmailCommand(
            userId,
            Token);
        _confirmationServiceMock
            .Setup(service => service.ConfirmAsync(
                userId,
                Token,
                cancellationToken))
            .ReturnsAsync(false);

        Task action()
        {
            return _handler.Handle(
            command,
            cancellationToken);
        }

        // Assert
        await Assert.ThrowsAsync<EmailConfirmationInvalidException>(action);
        _confirmationServiceMock.Verify(
            service => service.ConfirmAsync(
                userId,
                Token,
                cancellationToken),
            Times.Once);
        _confirmationServiceMock.VerifyNoOtherCalls();
    }
}
