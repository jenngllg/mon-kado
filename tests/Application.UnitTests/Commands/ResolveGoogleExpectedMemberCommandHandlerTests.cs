using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ResolveGoogleExpectedMemberCommandHandlerTests
{
    private readonly ResolveGoogleExpectedMemberCommandHandler _handler;
    private readonly Mock<IGoogleAccountSessionService> _googleAccountSessionServiceMock;

    public ResolveGoogleExpectedMemberCommandHandlerTests()
    {
        _googleAccountSessionServiceMock = new Mock<IGoogleAccountSessionService>(MockBehavior.Strict);
        _handler = new ResolveGoogleExpectedMemberCommandHandler(
            _googleAccountSessionServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenIdentityIsValidated_ReturnsExpectedMemberId()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var identity = new GoogleIdentity(
            "subject",
            "member@gmail.com",
            true,
            null,
            null);
        var expectedMemberId = Guid.CreateVersion7();
        var command = new ResolveGoogleExpectedMemberCommand(identity);
        _googleAccountSessionServiceMock
            .Setup(service => service.ResolveExpectedMemberIdAsync(
                identity,
                cancellationToken))
            .ReturnsAsync(expectedMemberId);

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Equal(
            expectedMemberId,
            result);
        _googleAccountSessionServiceMock.Verify(
            service => service.ResolveExpectedMemberIdAsync(
                identity,
                cancellationToken),
            Times.Once);
        _googleAccountSessionServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void CreateValidationException_WhenIdentityIsInvalid_ReturnsGenericFailure()
    {
        // Arrange
        var command = new ResolveGoogleExpectedMemberCommand(null);
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
}
