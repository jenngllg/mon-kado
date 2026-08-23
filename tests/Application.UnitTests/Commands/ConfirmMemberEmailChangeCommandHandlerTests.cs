using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ConfirmMemberEmailChangeCommandHandlerTests
{
    private readonly ConfirmMemberEmailChangeCommandHandler _handler;
    private readonly Mock<IMemberEmailChangeService> _memberEmailChangeServiceMock;

    public ConfirmMemberEmailChangeCommandHandlerTests()
    {
        _memberEmailChangeServiceMock = new Mock<IMemberEmailChangeService>(MockBehavior.Strict);
        _handler = new ConfirmMemberEmailChangeCommandHandler(
            _memberEmailChangeServiceMock.Object,
            NullLogger<ConfirmMemberEmailChangeCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenConfirmationIsValid_ConfirmsRequest()
    {
        // Arrange
        var requestId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new ConfirmMemberEmailChangeCommand(
            requestId,
            "encoded-token");
        _memberEmailChangeServiceMock
            .Setup(service => service.ConfirmAsync(
                requestId,
                "encoded-token",
                cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _memberEmailChangeServiceMock.Verify(
            service => service.ConfirmAsync(
                requestId,
                "encoded-token",
                cancellationToken),
            Times.Once);
        _memberEmailChangeServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenConfirmationIsInvalid_ThrowsGenericException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new ConfirmMemberEmailChangeCommand(
            null,
            null);
        _memberEmailChangeServiceMock
            .Setup(service => service.ConfirmAsync(
                Guid.Empty,
                string.Empty,
                cancellationToken))
            .ReturnsAsync(false);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<MemberEmailChangeInvalidException>(action);
        _memberEmailChangeServiceMock.Verify(
            service => service.ConfirmAsync(
                Guid.Empty,
                string.Empty,
                cancellationToken),
            Times.Once);
        _memberEmailChangeServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void CreateValidationException_WhenCommandIsInvalid_ReturnsGenericException()
    {
        // Arrange
        var command = new ConfirmMemberEmailChangeCommand(
            null,
            null);
        var validationErrors = Array.Empty<ValidationError>();

        // Act
        var exception = ((IGenericValidationFailure)command)
            .CreateValidationException(validationErrors);

        // Assert
        Assert.IsType<MemberEmailChangeInvalidException>(exception);
        _memberEmailChangeServiceMock.VerifyNoOtherCalls();
    }
}
