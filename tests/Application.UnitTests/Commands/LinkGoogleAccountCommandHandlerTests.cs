using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class LinkGoogleAccountCommandHandlerTests
{
    private readonly LinkGoogleAccountCommandHandler _handler;
    private readonly Mock<IGoogleAccountSessionService> _googleAccountSessionServiceMock;

    public LinkGoogleAccountCommandHandlerTests()
    {
        _googleAccountSessionServiceMock = new Mock<IGoogleAccountSessionService>(MockBehavior.Strict);
        _handler = new LinkGoogleAccountCommandHandler(
            _googleAccountSessionServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenLinkSucceeds_ReturnsTokensWithoutChangingPassword()
    {
        // Arrange
        const string Password = "  exact password  ";
        var cancellationToken = TestContext.Current.CancellationToken;
        var identity = CreateIdentity();
        var flowId = Guid.CreateVersion7();
        var command = new LinkGoogleAccountCommand(
            identity,
            false,
            "/my-lists",
            flowId,
            null,
            null,
            Password);
        var tokens = CreateTokens();
        _googleAccountSessionServiceMock
            .Setup(service => service.LinkAsync(
                It.Is<GoogleAuthenticationContext>(context =>
                    ReferenceEquals(
                        context.Identity,
                        identity) &&
                    !context.IsPersistent &&
                    context.FlowId == flowId),
                Password,
                cancellationToken))
            .ReturnsAsync(new GoogleAccountLinkResult(
                GoogleAccountLinkOutcome.Success,
                tokens));

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            tokens,
            result);
        _googleAccountSessionServiceMock.Verify(
            service => service.LinkAsync(
                It.Is<GoogleAuthenticationContext>(context =>
                    ReferenceEquals(
                        context.Identity,
                        identity) &&
                    !context.IsPersistent &&
                    context.FlowId == flowId),
                Password,
                cancellationToken),
            Times.Once);
        _googleAccountSessionServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(
        GoogleAccountLinkOutcome.InvalidCredentials,
        typeof(GoogleAccountLinkFailedException))]
    [InlineData(
        GoogleAccountLinkOutcome.Conflict,
        typeof(GoogleAccountLinkConflictException))]
    public async Task Handle_WhenLinkFails_ThrowsExpectedException(
        GoogleAccountLinkOutcome outcome,
        Type expectedException)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var identity = CreateIdentity();
        var command = new LinkGoogleAccountCommand(
            identity,
            false,
            "/",
            Guid.CreateVersion7(),
            null,
            null,
            "password");
        _googleAccountSessionServiceMock
            .Setup(service => service.LinkAsync(
                It.IsAny<GoogleAuthenticationContext>(),
                "password",
                cancellationToken))
            .ReturnsAsync(new GoogleAccountLinkResult(
                outcome,
                null));

        // Act
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => _handler.Handle(
            command,
            cancellationToken));

        // Assert
        Assert.IsType(
            expectedException,
            exception);
        _googleAccountSessionServiceMock.Verify(
            service => service.LinkAsync(
                It.IsAny<GoogleAuthenticationContext>(),
                "password",
                cancellationToken),
            Times.Once);
        _googleAccountSessionServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenSuccessHasNoTokens_ThrowsInvalidOperationException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new LinkGoogleAccountCommand(
            CreateIdentity(),
            false,
            "/",
            Guid.CreateVersion7(),
            null,
            null,
            "password");
        _googleAccountSessionServiceMock
            .Setup(service => service.LinkAsync(
                It.IsAny<GoogleAuthenticationContext>(),
                "password",
                cancellationToken))
            .ReturnsAsync(new GoogleAccountLinkResult(
                GoogleAccountLinkOutcome.Success,
                null));

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(action);
        _googleAccountSessionServiceMock.Verify(
            service => service.LinkAsync(
                It.IsAny<GoogleAuthenticationContext>(),
                "password",
                cancellationToken),
            Times.Once);
        _googleAccountSessionServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("currentPassword", typeof(RequestValidationException))]
    [InlineData("identity", typeof(GoogleAuthenticationFailedException))]
    public void CreateValidationException_WhenCommandIsInvalid_ReturnsExpectedFailure(
        string propertyName,
        Type expectedException)
    {
        // Arrange
        var command = new LinkGoogleAccountCommand(
            null,
            false,
            null,
            Guid.Empty,
            null,
            null,
            null);
        var validationFailure = (IGenericValidationFailure)command;

        // Act
        var exception = validationFailure.CreateValidationException(
            [
                new ValidationError(
                    propertyName,
                    "invalid")
            ]);

        // Assert
        Assert.IsType(
            expectedException,
            exception);
        _googleAccountSessionServiceMock.VerifyNoOtherCalls();
    }

    private static GoogleIdentity CreateIdentity()
    {

        return new GoogleIdentity(
            "google-subject",
            "member@example.com",
            true,
            null,
            "Member");
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
            false);
    }
}
