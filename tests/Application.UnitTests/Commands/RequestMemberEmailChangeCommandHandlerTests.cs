using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class RequestMemberEmailChangeCommandHandlerTests
{
    private readonly RequestMemberEmailChangeCommandHandler _handler;
    private readonly Mock<IMemberEmailChangeService> _memberEmailChangeServiceMock;

    public RequestMemberEmailChangeCommandHandlerTests()
    {
        _memberEmailChangeServiceMock = new Mock<IMemberEmailChangeService>(MockBehavior.Strict);
        _handler = new RequestMemberEmailChangeCommandHandler(
            _memberEmailChangeServiceMock.Object,
            NullLogger<RequestMemberEmailChangeCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenMemberExists_RequestsNormalizedEmailChange()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new RequestMemberEmailChangeCommand(
            memberId,
            " new@example.fr ",
            "current-password",
            42);
        _memberEmailChangeServiceMock
            .Setup(service => service.RequestAsync(
                memberId,
                "new@example.fr",
                "current-password",
                42,
                cancellationToken))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _memberEmailChangeServiceMock.Verify(
            service => service.RequestAsync(
                memberId,
                "new@example.fr",
                "current-password",
                42,
                cancellationToken),
            Times.Once);
        _memberEmailChangeServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberDoesNotExist_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new RequestMemberEmailChangeCommand(
            memberId,
            null,
            null,
            42);
        _memberEmailChangeServiceMock
            .Setup(service => service.RequestAsync(
                memberId,
                string.Empty,
                string.Empty,
                42,
                cancellationToken))
            .ReturnsAsync(false);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _memberEmailChangeServiceMock.Verify(
            service => service.RequestAsync(
                memberId,
                string.Empty,
                string.Empty,
                42,
                cancellationToken),
            Times.Once);
        _memberEmailChangeServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateValidationException_WhenCommandIsInvalid_ReturnsExpectedException(
        bool memberIdIsEmpty)
    {
        // Arrange
        var command = new RequestMemberEmailChangeCommand(
            memberIdIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            null,
            null,
            0);
        var validationErrors = new[]
        {
            new ValidationError(
                "Email",
                "The email is invalid.")
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
        _memberEmailChangeServiceMock.VerifyNoOtherCalls();
    }
}
