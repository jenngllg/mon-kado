using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CompleteGoogleAuthenticationCommandHandlerTests
{
    private readonly CompleteGoogleAuthenticationCommandHandler _handler;
    private readonly Mock<IGoogleAccountSessionService> _googleAccountSessionServiceMock;

    public CompleteGoogleAuthenticationCommandHandlerTests()
    {
        _googleAccountSessionServiceMock = new Mock<IGoogleAccountSessionService>(MockBehavior.Strict);
        _handler = new CompleteGoogleAuthenticationCommandHandler(
            _googleAccountSessionServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenCommandIsValid_ForwardsProtectedContext()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var identity = CreateIdentity();
        var flowId = Guid.CreateVersion7();
        var currentSessionId = Guid.CreateVersion7();
        var expected = new GoogleAuthenticationResult(
            GoogleAuthenticationOutcome.ExplicitLinkRequired,
            null);
        var command = new CompleteGoogleAuthenticationCommand(
            identity,
            true,
            "/my-lists",
            flowId,
            null,
            currentSessionId);
        _googleAccountSessionServiceMock
            .Setup(service => service.CompleteAsync(
                It.Is<GoogleAuthenticationContext>(context =>
                    ReferenceEquals(
                        context.Identity,
                        identity) &&
                    context.IsPersistent &&
                    context.ReturnPath == "/my-lists" &&
                    context.FlowId == flowId &&
                    context.CurrentSessionId == currentSessionId),
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _googleAccountSessionServiceMock.Verify(
            service => service.CompleteAsync(
                It.Is<GoogleAuthenticationContext>(context =>
                    ReferenceEquals(
                        context.Identity,
                        identity) &&
                    context.IsPersistent &&
                    context.ReturnPath == "/my-lists" &&
                    context.FlowId == flowId &&
                    context.CurrentSessionId == currentSessionId),
                cancellationToken),
            Times.Once);
        _googleAccountSessionServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void CreateValidationException_WhenProtectedContextIsInvalid_ReturnsGenericFailure()
    {
        // Arrange
        var command = new CompleteGoogleAuthenticationCommand(
            null,
            false,
            null,
            Guid.Empty,
            null,
            null);
        var validationFailure = (IGenericValidationFailure)command;

        // Act
        var exception = validationFailure.CreateValidationException(
            [
                new ValidationError(
                    "identity",
                    "invalid")
            ]);

        // Assert
        Assert.IsType<GoogleAuthenticationFailedException>(exception);
        _googleAccountSessionServiceMock.VerifyNoOtherCalls();
    }

    private static GoogleIdentity CreateIdentity()
    {

        return new GoogleIdentity(
            "google-subject",
            "member@gmail.com",
            true,
            null,
            "Member");
    }
}
