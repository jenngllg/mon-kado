using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpdateMemberPasswordCommandHandlerTests
{
    private readonly UpdateMemberPasswordCommandHandler _handler;
    private readonly Mock<IMemberPasswordService> _memberPasswordServiceMock;

    public UpdateMemberPasswordCommandHandlerTests()
    {
        _memberPasswordServiceMock = new Mock<IMemberPasswordService>(MockBehavior.Strict);
        _handler = new UpdateMemberPasswordCommandHandler(
            _memberPasswordServiceMock.Object,
            NullLogger<UpdateMemberPasswordCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenMemberExists_ChangesPasswordWithoutNormalizingValues()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new UpdateMemberPasswordCommand(
            memberId,
            " current password ",
            " new secure password ");
        _memberPasswordServiceMock
            .Setup(service => service.ChangeAsync(
                memberId,
                " current password ",
                " new secure password ",
                cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _memberPasswordServiceMock.Verify(
            service => service.ChangeAsync(
                memberId,
                " current password ",
                " new secure password ",
                cancellationToken),
            Times.Once);
        _memberPasswordServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberDoesNotExist_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new UpdateMemberPasswordCommand(
            memberId,
            "current password",
            "new secure password");
        _memberPasswordServiceMock
            .Setup(service => service.ChangeAsync(
                memberId,
                "current password",
                "new secure password",
                cancellationToken))
            .ReturnsAsync(false);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _memberPasswordServiceMock.Verify(
            service => service.ChangeAsync(
                memberId,
                "current password",
                "new secure password",
                cancellationToken),
            Times.Once);
        _memberPasswordServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateValidationException_WhenCommandIsInvalid_ReturnsExpectedException(
        bool memberIdIsEmpty)
    {
        // Arrange
        var command = new UpdateMemberPasswordCommand(
            memberIdIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            null,
            null);
        var validationErrors = new[]
        {
            new ValidationError(
                "newPassword",
                "The password is invalid.")
        };

        // Act
        var exception = ((IGenericValidationFailure)command)
            .CreateValidationException(validationErrors);

        // Assert
        Assert.Equal(
            memberIdIsEmpty
                ? typeof(InvalidAuthenticationSessionException)
                : typeof(RequestValidationException),
            exception.GetType());
        _memberPasswordServiceMock.VerifyNoOtherCalls();
    }
}
