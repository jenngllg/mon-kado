using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ReauthenticateTwoFactorCommandHandlerTests
{
    private readonly Mock<ITwoFactorService> _twoFactorServiceMock;
    private readonly ReauthenticateTwoFactorCommandHandler _handler;
    private readonly ReauthenticateTwoFactorCommand _request;

    public ReauthenticateTwoFactorCommandHandlerTests()
    {
        _twoFactorServiceMock = new Mock<ITwoFactorService>(MockBehavior.Strict);
        _handler = new ReauthenticateTwoFactorCommandHandler(
            _twoFactorServiceMock.Object,
            NullLogger<ReauthenticateTwoFactorCommandHandler>.Instance);
        _request = new ReauthenticateTwoFactorCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            TwoFactorFlowPurpose.ReplaceAuthenticator,
            "123456",
            null);
    }

    [Fact]
    public async Task Handle_WhenServiceSucceeds_ReturnsExactResultAndForwardsCancellation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = TestFixture.Create().Create<TwoFactorChallengeResponse>();
        _twoFactorServiceMock
            .Setup(service => service.ReauthenticateAsync(
                _request.MemberId,
                _request.AccessTokenId,
                _request.Purpose!.Value,
                _request.Code,
                _request.RecoveryCode,
                cancellationToken))
            .ReturnsAsync(result);

        // Act
        var actual = await _handler.Handle(
            _request,
            cancellationToken);

        // Assert
        Assert.Same(
            result,
            actual);
        _twoFactorServiceMock.Verify(
            service => service.ReauthenticateAsync(
                _request.MemberId,
                _request.AccessTokenId,
                _request.Purpose!.Value,
                _request.Code,
                _request.RecoveryCode,
                cancellationToken),
            Times.Once);
        _twoFactorServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenServiceFails_DoesNotRetryOrTransformFailure()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new InvalidOperationException("The operation could not be confirmed.");
        _twoFactorServiceMock
            .Setup(service => service.ReauthenticateAsync(
                _request.MemberId,
                _request.AccessTokenId,
                _request.Purpose!.Value,
                _request.Code,
                _request.RecoveryCode,
                cancellationToken))
            .ThrowsAsync(failure);

        // Act
        var actual = await Record.ExceptionAsync(() => _handler.Handle(
            _request,
            cancellationToken));

        // Assert
        Assert.Same(
            failure,
            actual);
        _twoFactorServiceMock.Verify(
            service => service.ReauthenticateAsync(
                _request.MemberId,
                _request.AccessTokenId,
                _request.Purpose!.Value,
                _request.Code,
                _request.RecoveryCode,
                cancellationToken),
            Times.Once);
        _twoFactorServiceMock.VerifyNoOtherCalls();
    }
}
