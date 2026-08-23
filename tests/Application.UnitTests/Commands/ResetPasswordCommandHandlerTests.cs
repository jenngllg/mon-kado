using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ResetPasswordCommandHandlerTests
{
    private readonly ResetPasswordCommandHandler _handler;
    private readonly Mock<IPasswordResetService> _passwordResetServiceMock;

    public ResetPasswordCommandHandlerTests()
    {
        _passwordResetServiceMock = new Mock<IPasswordResetService>(MockBehavior.Strict);
        _handler = new ResetPasswordCommandHandler(
            _passwordResetServiceMock.Object,
            NullLogger<ResetPasswordCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenResetLinkIsValid_PreservesSubmittedValues()
    {
        // Arrange
        var userId = Guid.CreateVersion7().ToString("D");
        const string Token = "AbCd_-0123";
        const string NewPassword = " new secure password ";
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new ResetPasswordCommand(
            userId,
            Token,
            NewPassword);
        _passwordResetServiceMock
            .Setup(service => service.ResetAsync(
                userId,
                Token,
                NewPassword,
                cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _passwordResetServiceMock.Verify(
            service => service.ResetAsync(
                userId,
                Token,
                NewPassword,
                cancellationToken),
            Times.Once);
        _passwordResetServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenResetLinkIsRejected_ThrowsPasswordResetInvalidException()
    {
        // Arrange
        var userId = Guid.CreateVersion7().ToString("D");
        const string Token = "AbCd_-0123";
        const string NewPassword = "new secure password";
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new ResetPasswordCommand(
            userId,
            Token,
            NewPassword);
        _passwordResetServiceMock
            .Setup(service => service.ResetAsync(
                userId,
                Token,
                NewPassword,
                cancellationToken))
            .ReturnsAsync(false);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<PasswordResetInvalidException>(action);
        _passwordResetServiceMock.Verify(
            service => service.ResetAsync(
                userId,
                Token,
                NewPassword,
                cancellationToken),
            Times.Once);
        _passwordResetServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("userId", typeof(PasswordResetInvalidException))]
    [InlineData("token", typeof(PasswordResetInvalidException))]
    [InlineData("newPassword", typeof(RequestValidationException))]
    public void CreateValidationException_WhenCommandIsInvalid_ReturnsExpectedException(
        string propertyName,
        Type expectedExceptionType)
    {
        // Arrange
        var command = new ResetPasswordCommand(
            null,
            null,
            null);
        var errors = new[]
        {
            new ValidationError(
                propertyName,
                "The value is invalid.")
        };

        // Act
        var exception = ((IGenericValidationFailure)command)
            .CreateValidationException(errors);

        // Assert
        Assert.IsType(
            expectedExceptionType,
            exception);
        _passwordResetServiceMock.VerifyNoOtherCalls();
    }
}
